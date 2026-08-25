#!/usr/bin/env python3
from __future__ import annotations
import argparse, json, struct
from pathlib import Path
from collections import defaultdict
from swf_extract_bitmaps import unpack_swf, iter_tags, u16, u32

BITMAP_TAGS={6,20,21,35,36,90}
SHAPE_TAGS={2,22,32,83}
SPRITE_TAG=39
SYMBOL_CLASS=76
EXPORT_ASSETS=56

class Bits:
    def __init__(self,b:bytes,byte=0): self.b=b; self.bit=byte*8
    def read(self,n):
        v=0
        for _ in range(n):
            i=self.bit>>3; sh=7-(self.bit&7); self.bit+=1
            if i>=len(self.b): raise EOFError
            v=(v<<1)|((self.b[i]>>sh)&1)
        return v
    def align(self): self.bit=(self.bit+7)&~7
    @property
    def pos(self): return self.bit>>3

def skip_rect(b,off):
    bits=Bits(b,off); n=bits.read(5); bits.read(n*4); bits.align(); return bits.pos

def skip_matrix(b,off):
    bits=Bits(b,off)
    if bits.read(1): n=bits.read(5); bits.read(n*2)
    if bits.read(1): n=bits.read(5); bits.read(n*2)
    n=bits.read(5); bits.read(n*2); bits.align(); return bits.pos

def skip_cxform(b,off,alpha=False):
    bits=Bits(b,off); add=bits.read(1); mul=bits.read(1); n=bits.read(4); comps=4 if alpha else 3
    if mul: bits.read(n*comps)
    if add: bits.read(n*comps)
    bits.align(); return bits.pos

def read_cstr(b,off):
    e=b.find(b'\0',off)
    if e<0: return '',len(b)
    return b[off:e].decode('utf-8','replace'),e+1

def rgba_len(shape_code): return 3 if shape_code in (2,22) else 4

def skip_gradient(b,off,shape_code,focal=False):
    off=skip_matrix(b,off)
    if off>=len(b): return off
    flags=b[off]; off+=1; n=flags&0x0f
    clen=rgba_len(shape_code)
    off += n*(1+clen)
    if focal: off+=2
    return off

def parse_fill_style(b,off,shape_code,refs:set[int]):
    if off>=len(b): return len(b)
    typ=b[off]; off+=1
    clen=rgba_len(shape_code)
    if typ==0x00: return min(len(b),off+clen)
    if typ in (0x10,0x12): return skip_gradient(b,off,shape_code,False)
    if typ==0x13: return skip_gradient(b,off,shape_code,True)
    if typ in (0x40,0x41,0x42,0x43):
        if off+2<=len(b): refs.add(u16(b,off)); off+=2
        return skip_matrix(b,off)
    return off

def parse_fill_array(b,off,shape_code,refs:set[int]):
    if off>=len(b): return len(b)
    n=b[off]; off+=1
    if n==0xff:
        if off+2>len(b): return len(b)
        n=u16(b,off); off+=2
    for _ in range(n): off=parse_fill_style(b,off,shape_code,refs)
    return off

def parse_line_array(b,off,shape_code,refs:set[int]):
    if off>=len(b): return len(b)
    n=b[off]; off+=1
    if n==0xff:
        if off+2>len(b): return len(b)
        n=u16(b,off); off+=2
    clen=rgba_len(shape_code)
    for _ in range(n):
        if off+2>len(b): return len(b)
        off+=2 # width
        if shape_code!=83:
            off+=clen
        else:
            if off+2>len(b): return len(b)
            flags=u16(b,off); off+=2
            join=(flags>>4)&3
            has_fill=(flags>>3)&1
            if join==2: off+=2
            if has_fill: off=parse_fill_style(b,off,shape_code,refs)
            else: off+=4
    return off

def shape_bitmap_refs(payload,code):
    refs=set()
    try:
        off=2
        off=skip_rect(payload,off)
        if code==83:
            off=skip_rect(payload,off); off+=1
        off=parse_fill_array(payload,off,code,refs)
        off=parse_line_array(payload,off,code,refs)
        # NewStyles later in shape records can add refs. A conservative byte scan is used
        # only for known bitmap IDs by caller if initial styles do not expose anything.
    except Exception:
        pass
    return refs

def place_ref(code,p):
    try:
        if code==4 and len(p)>=4: return {u16(p,0)}
        if code==26 and len(p)>=3:
            flags=p[0]; off=3
            if flags&0x02 and off+2<=len(p): return {u16(p,off)}
        if code==70 and len(p)>=4:
            f1=p[0]; f2=p[1]; off=4
            # PlaceObject3 className can precede character id when hasClassName or image+character
            if (f2&0x08) or ((f2&0x10) and (f1&0x02)):
                _,off=read_cstr(p,off)
            if f1&0x02 and off+2<=len(p): return {u16(p,off)}
    except Exception: pass
    return set()

def button_refs(code,p):
    refs=set()
    try:
        off=2
        if code==34:
            off+=1 # trackAsMenu/reserved
            action_offset=u16(p,off); off+=2
        while off<len(p):
            flags=p[off]; off+=1
            if flags==0: break
            # DefineButton2 has filter/blend flags in high bits; state flags low 4.
            if off+4>len(p): break
            cid=u16(p,off); refs.add(cid); off+=4 # cid+depth
            off=skip_matrix(p,off)
            if code==34:
                off=skip_cxform(p,off,True)
                if flags&0x10: # filter list, hard to skip safely -> stop after capturing cid
                    break
                if flags&0x20: off+=1
    except Exception: pass
    return refs

def nested_tags(sprite_payload):
    # DefineSprite: id UI16, framecount UI16, then normal tag stream without RECT/header
    off=4
    while off+2<=len(sprite_payload):
        h=u16(sprite_payload,off); off+=2; code=h>>6; ln=h&0x3f
        if ln==0x3f:
            if off+4>len(sprite_payload): break
            ln=u32(sprite_payload,off); off+=4
        p=sprite_payload[off:off+ln]; off+=ln
        yield code,p
        if code==0: break

def main():
    ap=argparse.ArgumentParser(); ap.add_argument('swf',type=Path); ap.add_argument('out',type=Path); a=ap.parse_args()
    data=unpack_swf(a.swf.read_bytes())
    deps=defaultdict(set); symbols={}; exports={}; bitmaps=set(); payloads={}
    for code,p,_ in iter_tags(data):
        if len(p)>=2 and code in BITMAP_TAGS|SHAPE_TAGS|{39,7,34}: payloads[(code,u16(p,0))]=p
        if code in BITMAP_TAGS and len(p)>=2: bitmaps.add(u16(p,0))
        elif code in SHAPE_TAGS and len(p)>=2: deps[u16(p,0)] |= shape_bitmap_refs(p,code)
        elif code==39 and len(p)>=4:
            sid=u16(p,0)
            for nc,np in nested_tags(p): deps[sid] |= place_ref(nc,np)
        elif code in (7,34) and len(p)>=2:
            deps[u16(p,0)] |= button_refs(code,p)
        elif code in (SYMBOL_CLASS,EXPORT_ASSETS) and len(p)>=2:
            n=u16(p,0); off=2
            target=symbols if code==SYMBOL_CLASS else exports
            for _ in range(n):
                if off+2>len(p): break
                cid=u16(p,off); off+=2; name,off=read_cstr(p,off); target[name]=cid
    # Conservative fallback for shape refs: only scan UI16-aligned/unaligned known bitmap IDs if parser found none.
    # This is marked candidate, not authoritative.
    candidate=defaultdict(set)
    for (code,cid),p in payloads.items():
        if code in SHAPE_TAGS and not deps[cid]:
            for i in range(2,max(2,len(p)-1)):
                v=p[i]|(p[i+1]<<8)
                if v in bitmaps: candidate[cid].add(v)
    def closure(cid):
        seen=set(); finals=set(); stack=[cid]
        while stack:
            x=stack.pop()
            if x in seen: continue
            seen.add(x)
            if x in bitmaps: finals.add(x); continue
            nxt=set(deps.get(x,()))
            if not nxt: nxt=set(candidate.get(x,()))
            stack.extend(nxt-seen)
        return sorted(finals),sorted(seen-{cid})
    rows=[]
    for kind,m in [('SymbolClass',symbols),('ExportAssets',exports)]:
        for name,cid in sorted(m.items()):
            imgs,chain=closure(cid)
            rows.append({'kind':kind,'name':name,'character_id':cid,'bitmap_ids':imgs,'dependency_ids':chain})
    a.out.parent.mkdir(parents=True,exist_ok=True)
    a.out.write_text(json.dumps({'swf':str(a.swf),'bitmap_ids':sorted(bitmaps),'symbols':rows},ensure_ascii=False,indent=2),encoding='utf-8')
    print(f'{a.swf.name}: {len(rows)} exported symbols, {len(bitmaps)} bitmaps -> {a.out}')
    for r in rows[:30]: print(r['name'],r['character_id'],'=>',r['bitmap_ids'][:12])
if __name__=='__main__': main()

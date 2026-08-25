#!/usr/bin/env python3
from __future__ import annotations
import argparse, io, json, lzma, struct, zlib
from pathlib import Path
from PIL import Image


def u16(b,o=0): return struct.unpack_from('<H',b,o)[0]
def u32(b,o=0): return struct.unpack_from('<I',b,o)[0]

def unpack_swf(raw: bytes) -> bytes:
    if len(raw)<8: raise ValueError('short SWF')
    sig=raw[:3]
    if sig==b'FWS': return raw
    if sig==b'CWS': return b'FWS'+raw[3:8]+zlib.decompress(raw[8:])
    if sig==b'ZWS':
        # ZWS: 8-byte header, compressed-length UI32, 5-byte LZMA props, stream.
        comp_len=u32(raw,8); props=raw[12:17]; body=raw[17:17+comp_len-5]
        if len(props)!=5: raise ValueError('bad ZWS props')
        prop0=props[0]; lc=prop0%9; rem=prop0//9; lp=rem%5; pb=rem//5
        dict_size=u32(props,1)
        dec=lzma.decompress(body, format=lzma.FORMAT_RAW, filters=[{'id':lzma.FILTER_LZMA1,'dict_size':dict_size,'lc':lc,'lp':lp,'pb':pb}])
        return b'FWS'+raw[3:8]+dec
    raise ValueError(f'unknown signature {sig!r}')

def rect_end(data: bytes, off=8) -> int:
    nbits=data[off]>>3
    bits=5+nbits*4
    return off+(bits+7)//8

def iter_tags(data: bytes):
    off=rect_end(data)
    off += 4 # frame rate UI16 + frame count UI16
    while off+2<=len(data):
        h=u16(data,off); off+=2
        code=h>>6; ln=h&0x3f
        if ln==0x3f:
            if off+4>len(data): break
            ln=u32(data,off); off+=4
        payload=data[off:off+ln]; tag_off=off; off+=ln
        yield code,payload,tag_off
        if code==0: break

def save_jpeg(blob: bytes, path: Path):
    # Some SWFs contain FF D9 FF D8 boundary garbage; normalize if present.
    blob=blob.replace(b'\xff\xd9\xff\xd8', b'') if blob.startswith(b'\xff\xd9\xff\xd8') else blob
    im=Image.open(io.BytesIO(blob)); im.load(); im.convert('RGBA').save(path)

def unpremul(c,a): return 0 if a==0 else min(255,(c*255 + a//2)//a)

def decode_lossless(payload: bytes, has_alpha: bool) -> Image.Image:
    cid=u16(payload,0); fmt=payload[2]; w=u16(payload,3); h=u16(payload,5); pos=7
    if fmt==3:
        table_n=payload[pos]+1; pos+=1
        raw=zlib.decompress(payload[pos:]); p=0
        palette=[]
        for _ in range(table_n):
            if has_alpha:
                r,g,b,a=raw[p:p+4]; p+=4
                palette.append((unpremul(r,a),unpremul(g,a),unpremul(b,a),a))
            else:
                r,g,b=raw[p:p+3]; p+=3; palette.append((r,g,b,255))
        stride=(w+3)&~3
        out=bytearray(w*h*4); q=0
        for y in range(h):
            row=raw[p+y*stride:p+y*stride+w]
            for idx in row:
                out[q:q+4]=bytes(palette[idx]); q+=4
        return Image.frombytes('RGBA',(w,h),bytes(out))
    raw=zlib.decompress(payload[pos:])
    if fmt==4:
        stride=((w*2)+3)&~3; out=bytearray(w*h*4); q=0
        for y in range(h):
            for x in range(w):
                v=struct.unpack_from('<H',raw,y*stride+x*2)[0]
                r=((v>>10)&31)*255//31; g=((v>>5)&31)*255//31; b=(v&31)*255//31
                out[q:q+4]=bytes((r,g,b,255)); q+=4
        return Image.frombytes('RGBA',(w,h),bytes(out))
    if fmt==5:
        out=bytearray(w*h*4); q=0; p=0
        for _ in range(w*h):
            if has_alpha:
                a,r,g,b=raw[p:p+4]; p+=4
                out[q:q+4]=bytes((unpremul(r,a),unpremul(g,a),unpremul(b,a),a))
            else:
                _,r,g,b=raw[p:p+4]; p+=4; out[q:q+4]=bytes((r,g,b,255))
            q+=4
        return Image.frombytes('RGBA',(w,h),bytes(out))
    raise ValueError(f'unsupported lossless fmt={fmt}, id={cid}')

def extract(path: Path, out: Path):
    data=unpack_swf(path.read_bytes()); out.mkdir(parents=True,exist_ok=True)
    manifest=[]; jpeg_tables=None
    for code,payload,tag_off in iter_tags(data):
        try:
            if code==8: jpeg_tables=payload; continue
            if code==6: # DefineBits + JPEGTables
                cid=u16(payload); blob=payload[2:]
                if jpeg_tables: blob=jpeg_tables.rstrip(b'\xff\xd9')+blob.lstrip(b'\xff\xd8')
                dest=out/f'{cid:05d}.png'; save_jpeg(blob,dest); manifest.append({'id':cid,'tag':code,'file':dest.name})
            elif code==21: # DefineBitsJPEG2
                cid=u16(payload); dest=out/f'{cid:05d}.png'; save_jpeg(payload[2:],dest); manifest.append({'id':cid,'tag':code,'file':dest.name})
            elif code in (35,90): # JPEG3/JPEG4 + alpha
                cid=u16(payload); pos=2; alpha_off=u32(payload,pos); pos+=4
                if code==90: pos+=2 # deblock param
                image_blob=payload[pos:pos+alpha_off]; alpha_blob=payload[pos+alpha_off:]
                im=Image.open(io.BytesIO(image_blob)).convert('RGBA'); alpha=zlib.decompress(alpha_blob)
                if len(alpha)>=im.width*im.height:
                    im.putalpha(Image.frombytes('L',im.size,alpha[:im.width*im.height]))
                dest=out/f'{cid:05d}.png'; im.save(dest); manifest.append({'id':cid,'tag':code,'file':dest.name})
            elif code in (20,36):
                cid=u16(payload); im=decode_lossless(payload,code==36); dest=out/f'{cid:05d}.png'; im.save(dest); manifest.append({'id':cid,'tag':code,'file':dest.name})
        except Exception as e:
            manifest.append({'tag':code,'offset':tag_off,'error':type(e).__name__+': '+str(e)})
    (out/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
    ok=[x for x in manifest if 'file' in x]
    print(f'{path.name}: extracted {len(ok)} bitmap(s) -> {out}')
    return len(ok)

def main():
    ap=argparse.ArgumentParser(); ap.add_argument('swf',type=Path); ap.add_argument('out',type=Path); a=ap.parse_args(); extract(a.swf,a.out)
if __name__=='__main__': main()

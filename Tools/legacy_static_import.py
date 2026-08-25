#!/usr/bin/env python3
from __future__ import annotations
import json, re, sys
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_XML = ROOT / 'LegacyReference/GCLDServer/wwwroot/gcld.3.1/sdata/gcld_xs_sdata.xml'
DEFAULT_NAMES = ROOT / 'LegacyReference/GCLDServer/server/apps/NameData.xml'
OUT = ROOT / 'Data/Canonical'
WORLD_CITY_LAYOUT = ROOT / 'LegacyReference/GCLDServer/wwwroot/assets/zh_CN/xml/world/CityInfo.xml'


def rows(root, table):
    node = root.find(f".//table[@name='{table}']")
    if node is None:
        return []
    result=[]
    for row in node.findall('row'):
        result.append({f.get('name',''): (f.text or '') for f in row.findall('field')})
    return result

def as_int(v, default=0):
    try: return int(v)
    except: return default

def as_float(v, default=0.0):
    try: return float(v)
    except: return default

def parse_reward(text):
    out=[]
    for chunk in (text or '').split(';'):
        chunk=chunk.strip()
        if not chunk: continue
        p=[x.strip() for x in chunk.split(',')]
        kind=p[0]
        args=[]
        for x in p[1:]:
            try: args.append(int(x))
            except: args.append(x)
        out.append({'kind':kind,'args':args})
    return out

def parse_target(text):
    raw=(text or '').strip()
    p=[x.strip() for x in raw.split(',') if x.strip()]
    if not p: return {'kind':'','args':[],'raw':raw}
    args=[]
    for x in p[1:]:
        try: args.append(int(x))
        except: args.append(x)
    return {'kind':p[0],'args':args,'raw':raw}

def parse_related(text):
    return [as_int(x) for x in (text or '').split(',') if x.strip()]

def _parse_word_tones(raw: str):
    # Java legacy format: each token is WORD + intonation digits, where token length is 2/4/6/8.
    # Java splits the token in half: first half = word, second half = intonation integer.
    result=[]
    for token in (raw or '').replace('\r','').replace('\n','').replace('\t','').replace(' ','').split('|'):
        token=token.strip()
        if len(token) not in (2,4,6,8):
            continue
        half=len(token)//2
        word=token[:half]
        tone=token[half:]
        try:
            result.append({'word':word,'intonation':int(tone)})
        except ValueError:
            pass
    return result

def load_name_data(path: Path):
    root=ET.parse(path).getroot()
    def data(tag):
        node=root.find(tag)
        return '' if node is None else node.attrib.get('data','')
    return {
        'male': _parse_word_tones(data('male')),
        'female': _parse_word_tones(data('female')),
        'last': _parse_word_tones(data('lastname')),
        'uncommonLast': [x.strip() for x in data('incommon').replace('\r','').replace('\n','').replace('\t','').split('|') if x.strip()],
        'samples': _parse_word_tones(data('sample')),
    }

def main():
    xml=Path(sys.argv[1]) if len(sys.argv)>1 else DEFAULT_XML
    names=Path(sys.argv[2]) if len(sys.argv)>2 else DEFAULT_NAMES
    OUT.mkdir(parents=True,exist_ok=True)
    root=ET.parse(xml).getroot()

    buildings=[]
    for d in rows(root,'building'):
        buildings.append({
            'id':as_int(d.get('id')), 'name':d.get('name','').strip(), 'intro':d.get('intro','').strip(),
            'openLevel':as_int(d.get('open_lv')), 'position':as_int(d.get('pos')), 'areaType':as_int(d.get('type')),
            'outputType':as_int(d.get('output_type')), 'outputExponent':as_float(d.get('output_e')),
            'outputSeriesId':as_int(d.get('output_s')), 'outputRelatedFactor':as_float(d.get('output_e1')),
            'outputRelatedBuildings':parse_related(d.get('output_related_building')),
            'timeExponent':as_float(d.get('time_e')), 'timeBase':as_int(d.get('time_base')),
            'timeSeriesId':as_int(d.get('time_s')), 'timeRSeriesId':as_int(d.get('time_r')), 'timeTSeriesId':as_int(d.get('time_t')),
            'copperExponent':as_float(d.get('copper_e')), 'copperSeriesId':as_int(d.get('copper_s')),
            'woodExponent':as_float(d.get('lumber_e')), 'woodSeriesId':as_int(d.get('lumber_s')),
            'drawingId':as_int(d.get('drawing')),
            'chiefExpExponent':as_float(d.get('chief_exp_e')), 'chiefExpSeriesId':as_int(d.get('chief_exp_s')),
        })

    tasks=[]
    for d in rows(root,'task'):
        tasks.append({
            'id':as_int(d.get('ID')), 'name':d.get('Name','').strip(), 'nextTaskId':as_int(d.get('Next')),
            'area':as_int(d.get('area')), 'target':parse_target(d.get('Target')), 'reward':parse_reward(d.get('Reward')),
            'introLong':d.get('IntroL','').strip(), 'introShort':d.get('IntroS','').strip(), 'plot':d.get('plot','').strip(),
        })

    serial={}
    for d in rows(root,'serial'):
        sid=str(as_int(d.get('id'))); idx=str(as_int(d.get('index')))
        serial.setdefault(sid,{})[idx]=as_int(d.get('point'))

    functions=[]
    for d in rows(root,'function'):
        functions.append({'id':as_int(d.get('id')), 'intro':d.get('intro','').strip()})

    constants={}
    for d in rows(root,'c'):
        key=d.get('param','').strip()
        if key: constants[key]={'value':d.get('value','').strip(),'id':as_int(d.get('id')),'intro':d.get('intro','').strip()}

    generals=[]
    for d in rows(root,'general'):
        generals.append({
            'id':as_int(d.get('id')), 'name':d.get('name','').strip(), 'type':as_int(d.get('type')),
            'pic':d.get('pic','').strip(), 'quality':as_int(d.get('quality')), 'leader':as_int(d.get('leader')),
            'strength':as_int(d.get('strength')), 'intel':as_int(d.get('intel')), 'politics':as_int(d.get('politics')),
            'troopId':as_int(d.get('troop')), 'tacticId':as_int(d.get('tactic_id')), 'stratagemId':as_int(d.get('stratagem_id')),
            'upgradeExpSeriesId':as_int(d.get('up_exp_s')), 'upgradeExpExponent':as_float(d.get('up_exp_e')),
            'intro':d.get('intro','').strip(), 'broadcast':as_int(d.get('broad_cast'))
        })

    general_recruits=[]
    for d in rows(root,'general_recruit'):
        general_recruits.append({
            'id':as_int(d.get('id')), 'generalId':as_int(d.get('general_id')), 'type':as_int(d.get('type')),
            'powerId':as_int(d.get('power_id')), 'npcId':as_int(d.get('NPC_id')), 'dropIndex':as_int(d.get('drop_index')),
            'copperMin':as_int(d.get('copper_min')), 'copperMax':as_int(d.get('copper_max')),
            'goldMin':as_int(d.get('gold_min')), 'goldMax':as_int(d.get('gold_max')), 'goldProb':as_float(d.get('gold_prob')),
            'minRefreshTime':as_int(d.get('min_refur_time')), 'intro':d.get('intro','').strip()
        })

    equipment=[]
    for d in rows(root,'equip'):
        equipment.append({
            'id':as_int(d.get('id')), 'name':d.get('name','').strip(), 'type':as_int(d.get('type')), 'pic':d.get('pic','').strip(),
            'quality':as_int(d.get('quality')), 'level':as_int(d.get('level')), 'defaultLevel':as_int(d.get('default_level')),
            'maxLevel':as_int(d.get('max_level')), 'attribute':as_int(d.get('attribute')),
            'copperBuy':as_int(d.get('copper_buy')), 'copperSold':as_int(d.get('copper_sold')),
            'skillType':as_int(d.get('skill_type')), 'skillNum':as_int(d.get('skill_num')),
            'skillLevelDefault':as_int(d.get('skill_lv_default')), 'skillLevelMax':as_int(d.get('skill_lv_max')),
            'probBase':as_float(d.get('prob_base')), 'probIntimacy':as_float(d.get('prob_intimacy')),
            'intimacyGroup':as_int(d.get('intimacy_group')), 'intimacyGroupProb':as_float(d.get('intimacy_group_prob')),
            'intro':d.get('intro','').strip()
        })

    items=[]
    for d in rows(root,'items'):
        items.append({
            'id':as_int(d.get('id')), 'name':d.get('name','').strip(), 'type':as_int(d.get('type')),
            'index':as_int(d.get('index')), 'quality':as_int(d.get('quality')), 'pic':d.get('pic','').strip(),
            'copper':as_int(d.get('copper')), 'effect':d.get('effect','').strip(), 'intro':d.get('intro','').strip(),
            'changeItemId':as_int(d.get('change_item_id')), 'changeNum':as_int(d.get('change_num'))
        })

    technologies=[]
    for d in rows(root,'tech'):
        technologies.append({
            'id':as_int(d.get('id')), 'key':as_int(d.get('key')), 'keyString':d.get('key_str','').strip(),
            'name':d.get('name','').strip(), 'pic':d.get('pic','').strip(), 'intro':d.get('intro','').strip(),
            'researchTime':as_int(d.get('research_time')), 'resource':d.get('resource','').strip(),
            'resourceTimes':as_int(d.get('resource_times')), 'dropIndex':as_int(d.get('drop_index')),
            'parameters':[as_float(d.get(f'par_{i}')) for i in range(1,5)],
            'parameterIntros':[d.get(f'par_{i}_intro','').strip() for i in range(1,5)]
        })

    troops=[]
    for d in rows(root,'troop'):
        troops.append({
            'id':as_int(d.get('id')), 'name':d.get('name','').strip(), 'type':as_int(d.get('type')),
            'quality':as_int(d.get('quality')), 'level':as_int(d.get('level')), 'serial':as_int(d.get('serial')),
            'attack':as_int(d.get('att')), 'defense':as_int(d.get('def')), 'speed':as_int(d.get('speed')),
            'openLevel':as_int(d.get('open_lv')), 'terrainSpec':d.get('terrain_spec','').strip(),
            'terrainStrategy':d.get('terrain_strategy','').strip(), 'terrainStrategyDefense':d.get('terrain_stratege_defense','').strip(),
            'drop':d.get('drop','').strip()
        })

    tactics=[]
    for d in rows(root,'tactic'):
        tactics.append({
            'id':as_int(d.get('id')), 'name':d.get('name','').strip(), 'displayId':as_int(d.get('display_id')),
            'pic':d.get('pic','').strip(), 'basicPic':d.get('basic_pic','').strip(), 'range':as_int(d.get('range')),
            'playerTime':as_int(d.get('playertime')), 'damageExponent':as_float(d.get('damage_e')),
            'specialEffect':d.get('special_effect','').strip(), 'intro':d.get('intro','').strip()
        })

    world_layout = {}
    if WORLD_CITY_LAYOUT.exists():
        for city in ET.parse(WORLD_CITY_LAYOUT).getroot().findall('city'):
            world_layout[as_int(city.get('id'))] = {
                'x': as_int(city.get('x')), 'y': as_int(city.get('y')),
                'model': (city.get('model') or '').strip()
            }
    world_cities=[]
    for d in rows(root,'world_city'):
        layout = world_layout.get(as_int(d.get('id')), {})
        world_cities.append({
            'id':as_int(d.get('id')), 'name':d.get('name','').strip(), 'type':as_int(d.get('type')),
            'terrain':as_int(d.get('terrain')), 'terrainEffectType':as_int(d.get('terrain_effect_type')),
            'output':as_int(d.get('output')), 'chief':as_int(d.get('chief')),
            'npcs':[as_int(x) for x in (d.get('npcs') or '').split(';') if x.strip()],
            'weiDistance':as_int(d.get('wei_distance')), 'shuDistance':as_int(d.get('shu_distance')), 'wuDistance':as_int(d.get('wu_distance')),
            'weiArea':as_int(d.get('wei_area')), 'shuArea':as_int(d.get('shu_area')), 'wuArea':as_int(d.get('wu_area')),
            'weiMask':as_int(d.get('m_wei')), 'shuMask':as_int(d.get('m_shu')), 'wuMask':as_int(d.get('m_wu')),
            'showMask':as_int(d.get('show_mask')), 'pic':d.get('pic','').strip(), 'intro':d.get('intro','').strip(),
            'x':layout.get('x',0), 'y':layout.get('y',0), 'model':layout.get('model','')
        })


    general_positions=[]
    for d in rows(root,'general_position'):
        general_positions.append({
            'id':as_int(d.get('id')), 'type':as_int(d.get('type')), 'openLevel':as_int(d.get('open_lv')),
            'openTips':d.get('open_tips','').strip(), 'openIntro':d.get('open_intro','').strip()
        })

    tavern_stats=[]
    for d in rows(root,'tavern_stat'):
        tavern_stats.append({
            'preState':as_int(d.get('pre_stat')), 'nextState':as_int(d.get('next_stat')), 'probability':as_float(d.get('prob'))
        })

    charge_items=[]
    for d in rows(root,'chargeitem'):
        charge_items.append({
            'id':as_int(d.get('id')), 'name':d.get('name','').strip(), 'explain':d.get('explain','').strip(),
            'pic':d.get('pic','').strip(), 'ifShow':as_int(d.get('if_show')), 'param':as_int(d.get('param')),
            'alert':as_int(d.get('alert')), 'level':as_int(d.get('lv')), 'cost':as_int(d.get('cost')),
            'intro':d.get('intro','').strip()
        })

    string_constants=[]
    for d in rows(root,'string_c'):
        string_constants.append({
            'id':as_int(d.get('id')), 'value':d.get('value','').strip(), 'intro':d.get('intro','').strip(),
            'param':d.get('param','').strip(), 'system':d.get('system','').strip()
        })

    equip_suits=[]
    for d in rows(root,'equip_suit'):
        equip_suits.append({
            'id':as_int(d.get('id')), 'name':d.get('name','').strip(), 'minChiefLevel':as_int(d.get('min_chief_lv')),
            'type':as_int(d.get('type')), 'maxIntimacyLevel':as_int(d.get('max_intimacy_lv')),
            'equipmentIds':[as_int(x) for x in (d.get('equip_list') or '').split(';') if x.strip()],
            'quality':as_int(d.get('quality'))
        })

    store_items=[]
    for d in rows(root,'store_items'):
        store_items.append({
            'id':as_int(d.get('id')), 'itemId':as_int(d.get('item_id')), 'copper':as_int(d.get('copper')),
            'gold':as_int(d.get('gold')), 'goldProbability':as_float(d.get('gold_prob')),
            'minRefreshTime':as_int(d.get('min_refur_time'))
        })

    store_stats=[]
    for d in rows(root,'store_stat'):
        store_stats.append({
            'preState':as_int(d.get('pre_stat')), 'nextState':as_int(d.get('next_stat')), 'probability':as_float(d.get('prob'))
        })

    world_roads=[]
    for d in rows(root,'world_road'):
        world_roads.append({
            'id':as_int(d.get('id')), 'start':as_int(d.get('start')), 'end':as_int(d.get('end')), 'length':as_int(d.get('length')),
            'trace':d.get('trace','').strip(), 'weiReward':d.get('wei_reward','').strip(),
            'shuReward':d.get('shu_reward','').strip(), 'wuReward':d.get('wu_reward','').strip()
        })

    catalogs=[
        ('buildings.json',buildings),('tasks.json',tasks),('serial.json',serial),('functions.json',functions),('constants.json',constants),
        ('generals.json',generals),('general_recruits.json',general_recruits),('equipment.json',equipment),('items.json',items),
        ('technologies.json',technologies),('troops.json',troops),('tactics.json',tactics),('world_cities.json',world_cities),('world_roads.json',world_roads),
        ('general_positions.json',general_positions),('tavern_stats.json',tavern_stats),('charge_items.json',charge_items),('string_constants.json',string_constants),
        ('equip_suits.json',equip_suits),('store_items.json',store_items),('store_stats.json',store_stats)
    ]

    for name,obj in catalogs:
        (OUT/name).write_text(json.dumps(obj,ensure_ascii=False,indent=2),encoding='utf-8')
    if names.exists():
        (OUT/'names.json').write_text(json.dumps(load_name_data(names),ensure_ascii=False,indent=2),encoding='utf-8')

    print(f'Imported {len(buildings)} buildings, {len(tasks)} tasks, {len(generals)} generals, {len(equipment)} equips, {len(technologies)} techs, {len(troops)} troops, {len(tactics)} tactics, {len(world_cities)} cities, {len(world_roads)} roads, {len(tavern_stats)} tavern transitions, {len(equip_suits)} equip suits, {len(store_items)} store items, {len(store_stats)} store transitions.')
    print(OUT)

if __name__=='__main__': main()

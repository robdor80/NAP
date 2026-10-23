"""Offline verification: Python 3 + Pillow, run from repository root. No network or product operations."""
from pathlib import Path
import hashlib,json,xml.etree.ElementTree as ET,collections,subprocess,zipfile
from PIL import Image
D=Path(__file__).resolve().parent
m=json.loads((D/'C-Pruebas-evidencias/evidence.json').read_text())
sha=m['verified_source'];folder=D/'runs'/str(m['final_run'])
assert (folder/'source-sha.txt').read_text(encoding='utf-8-sig').strip()==sha
c=ET.parse(folder/'phase6-acceptance.trx').find('.//{*}Counters').attrib
assert c['total']==c['executed']==c['passed']=='157' and c['failed']==c['notExecuted']=='0'
by_size=collections.Counter();states=collections.Counter();pixels=set();hashes=set()
for p in folder.glob('*.png'):
 v=json.loads(p.with_name(p.name+'.json').read_text());h=hashlib.sha256(p.read_bytes()).hexdigest()
 assert h.upper()==v['sha256'].upper() and h not in hashes,p.name
 hashes.add(h)
 with Image.open(p) as im:
  assert im.size==(v['width'],v['height']) and im.size in [(1440,900),(1200,700)]
  pixelhash=hashlib.sha256(im.convert('RGBA').tobytes()).hexdigest();assert pixelhash not in pixels,p.name;pixels.add(pixelhash)
 by_size[im.size]+=1;states[v['mode']]+=1;assert v['bindingErrors']==0
 if 'zoom' not in v:
  expected='Ajustes' if v['mode'].startswith(('historical','diagnostics','licenses')) or v['mode']=='settings' else 'Catálogo' if v['mode']=='catalog' else 'Historial' if v['mode']=='history' or v['mode'].startswith('activity') else 'Producción' if v['mode'] in ['production','choice','exception','approved','blocked','empty','read-error','manual'] else 'Inicio'
  assert v['page']==expected,p.name
  if v['mode']=='blocked':assert not v['canApprove'] and not v['canContinue']
  if v['mode']=='approved':assert v['canContinue']
  if v['mode']=='diagnostics-filtered':assert v['diagnosticRows']==1
  if v['mode']=='diagnostics':assert v['diagnosticRows']==4
 if v['mode'].startswith('historical'):
  assert v['page']=='Ajustes' and v['section']=='Histórico sintético'
  assert v['historicalViewport']>= (180 if v['width']==1200 else 350)
 if 'zoom' in v:assert v['marginGuides']==2 and v['zoom'] in [1,2] and len(v['originalSha256'])==64
assert len(states)==38 and all(n>=2 for n in states.values())
assert len(hashes)==m['captures']
for p in folder.glob('*.stderr.txt'): assert p.stat().st_size==0,p.name
assert len(list(folder.glob('*.stdout.txt')))==76
for run in m['runs']:
 archive=D/'archives'/f"actions-{run['id']}.zip"
 assert 'sha256:'+hashlib.sha256(archive.read_bytes()).hexdigest()==run['artifact_digest']
 with zipfile.ZipFile(archive) as z:
  for entry in z.infolist():
   if not entry.is_dir():assert z.read(entry)==(D/'runs'/str(run['id'])/entry.filename).read_bytes(),entry.filename
assert (D/'C-Pruebas-evidencias/verified-build-workflow.yml').read_bytes()==subprocess.check_output(['git','show',sha+':.github/workflows/phase6-review.yml'],cwd=D.parents[1])
for rel,h in json.loads((D/'C-Pruebas-evidencias/verified-source-files.json').read_text()).items():
 assert hashlib.sha256((D.parents[1]/rel).read_bytes()).hexdigest()==h,rel
 assert hashlib.sha256(subprocess.check_output(['git','show',sha+':'+rel],cwd=D.parents[1])).hexdigest()==h,rel
for line in (D/'SHA256SUMS.txt').read_text().splitlines():
 h,rel=line.split('  ',1);assert hashlib.sha256((D/rel).read_bytes()).hexdigest()==h,rel
print('PASS:',sha,'157/157 targeted tests;',len(hashes),'distinct original PNGs;',dict(by_size),'; source, captures and delivery hashes verified')

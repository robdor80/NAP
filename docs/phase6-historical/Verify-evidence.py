"""Offline verification of P6.7 evidence only; does not run NAP or inspect any asset root."""
from pathlib import Path
import hashlib, json, xml.etree.ElementTree as ET
from PIL import Image
root=Path(__file__).resolve().parent
evidence=json.loads((root/'evidence.json').read_text())
for line in (root/'SHA256SUMS.txt').read_text().splitlines():
    digest,name=line.split('  ',1)
    target=root/name
    assert target.is_relative_to(root) and '..' not in Path(name).parts
    assert hashlib.sha256(target.read_bytes()).hexdigest()==digest,name
for run in evidence['runs']:
    out=root/('final' if run['final'] else 'preliminary/'+str(run['id']))
    assert (out/'source-sha.txt').read_text(encoding='utf-8-sig').strip()==run['sha']
    counts=ET.parse(out/'phase6-historical.trx').find('.//{*}Counters').attrib
    assert counts['total']==counts['passed']=='108' and counts['failed']==counts['notExecuted']=='0'
    assert len(list(out.glob('*.png')))==run['captures']
    assert all(p.stat().st_size==0 for p in out.glob('*.stderr.txt'))
    for line in (out/'captures.sha256').read_text(encoding='utf-8-sig').splitlines():
        digest,name=line.split('  ',1)
        assert hashlib.sha256((out/name).read_bytes()).hexdigest().upper()==digest.upper()
    for p in out.glob('*.png'):
        with Image.open(p) as im:
            assert im.size==((1200,700) if '1200x700' in p.name else (1440,900))
    for p in out.glob('*.stdout.txt'):
        for line in p.read_text(encoding='utf-8-sig').splitlines():
            if line.startswith('{'):
                data=json.loads(line); assert data['bindingErrors']==0 and data['synthetic'] is True
print('Verified: digests, source SHAs, focused TRX, capture counts, dimensions, binding logs and empty stderr.')

"""Verify preserved evidence bytes only. No app/host execution, network, writes or license reads."""
from pathlib import Path
import collections
import hashlib
import json
import re
import struct
import xml.etree.ElementTree as ET
root=Path(__file__).resolve().parent
for line in (root/'SHA256SUMS.txt').read_text().splitlines():
    digest,name=line.split('  ',1)
    assert hashlib.sha256((root/name).read_bytes()).hexdigest()==digest,name
facts=json.loads((root/'evidence.json').read_text())
final=root/'final'
assert (final/'source-sha.txt').read_text().strip()==facts['verifiedSource']
assert hashlib.sha256((root/facts['artifactFile']).read_bytes()).hexdigest()==facts['artifactSHA256']
assert hashlib.sha256((root/facts['trxFile']).read_bytes()).hexdigest()==facts['trxSHA256']
ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
tree=ET.parse(root/facts['trxFile'])
counts=tree.find('.//t:Counters',ns).attrib
assert counts['passed']==counts['total']=='61' and counts['failed']=='0' and counts['notExecuted']=='0'
assert len(list(final.glob('*.png')))==22
for p in final.glob('*.png'):
    content=p.read_bytes()
    assert content[:8]==b'\x89PNG\r\n\x1a\n'
    assert struct.unpack('>II',content[16:24])==tuple(map(int,re.search(r'(\d+)x(\d+)\.png$',p.name).groups()))
for line in (final/'captures.sha256').read_text().splitlines():
    digest,name=line.split(None,1)
    assert hashlib.sha256((final/name.strip()).read_bytes()).hexdigest().upper()==digest.upper()
assert len(list(final.glob('*.stderr.txt')))==22
assert all(p.stat().st_size==0 for p in final.glob('*.stderr.txt'))
for p in final.glob('*.stdout.txt'):
    for line in p.read_text().splitlines():
        if line.startswith('{'):
            render=json.loads(line)
            assert render['bindingErrors']==0 and render['synthetic']
for p in final.glob('diagnostics-export-*.stdout.txt'):
    assert 'diagnostic_native_clipboard_and_new_text_export_verified' in p.read_text()
a=final/'diagnostic-report-1440x900.txt'
b=final/'diagnostic-report-1200x700.txt'
assert a.read_bytes()==b.read_bytes()
for denied in ('private-token','alice','Alice','C:\\','/home/','queue_','attempt_','sixlabors.lic','Bearer ','BEGIN PRIVATE','NAP_GEMINI_API_KEY'):
    assert denied not in a.read_text(),denied
print('Verified: SHA-256 manifest, source, 61/61 TRX, 22 native PNG dimensions/hashes, bindings, stderr, clipboard/export proof and synthetic report privacy.')

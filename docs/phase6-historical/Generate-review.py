from pathlib import Path
import hashlib,json,subprocess,re,shutil,xml.etree.ElementTree as ET
from xml.sax.saxutils import escape
from reportlab.platypus import SimpleDocTemplate,Paragraph,Spacer,Image,PageBreak
from reportlab.lib.styles import getSampleStyleSheet,ParagraphStyle
from reportlab.lib import colors
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
r=Path.cwd(); assert (r/'src/NAP.Core').is_dir(); d=r/'docs/phase6-historical';out=d/'final'
code_sha='f3130ce7144644fb4179920c0332371ed0c5991c'
run_id=39870877507
p=d/'Informe-revision.md'
s=p.read_text().replace('PENDIENTE_RESULTADOS_FINALES',f'''**Código fuente efectivamente verificado: `{code_sha}`.** [Workflow Windows {run_id}](https://github.com/robdor80/NAP/actions/runs/{run_id}). Ambos builds Release aprobados; **108/108 pruebas Passed, cero fallos y cero omisiones**: 14 casos P6.7, 93 casos pequeños del resolver y un caso de navegación sin configuración. **34 PNG originales**, 17 en cada resolución, 18 procesos con stderr vacío, sin errores de binding. Los cinco advisories NU1902/NU1903 existentes permanecen visibles.

Evidencias preliminares preservadas: c49c9e4 (run 39870109208), f0f4ca3 (39870425854) y 329b3ae (39870560823), cada una con 108 pruebas Passed y 28 PNG. No hubo fallos de compilación o tests en estas ejecuciones. Los refinamientos posteriores documentan agrupación, etiquetas y capturas inferiores de incidencias, sin atribuir los resultados anteriores al SHA final. Los commits documentales de entrega posteriores conservan exactamente el producto verificado y no se presentan como nuevas ejecuciones.''')
p.write_text(s)
shutil.copyfile(out/'historical-simulation-1440x900.txt',d/'Informe-simulacion-anomalias.txt')
source_files=subprocess.check_output(['git','diff','--name-only','e0ef903',code_sha,'--','src','tests','.github/workflows','config','schemas'],cwd=r,text=True).splitlines()
source={f:hashlib.sha256((r/f).read_bytes()).hexdigest() for f in source_files}
(d/'verified-source-files.json').write_text(json.dumps(source,indent=2)+'\n')
runs=[]
for rid,sha,folder,captures,final,artifact,digest in [
 (39870109208,'c49c9e4328e2de67695db692e8f4aa4156b876c3','preliminary/39870109208',28,False,12466440137,'5831b5411f6ab8e6a6e2863f494ceb66588ae6e9fa8e0900ada9a3c78bac757b'),
 (39870425854,'f0f4ca3b146cf866bad489465b891691272bf58f','preliminary/39870425854',28,False,12466022405,'de6ce20392003cdda73ffde4aae6cb6c279b8f128bf7197b57b0ff29ab90956b'),
 (39870560823,'329b3ae85b9089328cd4f8496396aa04df0c8ed4','preliminary/39870560823',28,False,12466765169,'5c9b4d174ac81ca9c71844cc93ea193fcb3951adb5468fab4b372416308fd390'),
 (run_id,code_sha,'final',34,True,12467030140,'5021f4131389637dbe5f172bfe3997312cfa7fc62d929292280ac5c50248377a')]:
  counts=ET.parse(d/folder/'phase6-historical.trx').find('.//{*}Counters').attrib
  assert counts['passed']=='108' and counts['failed']=='0'
  runs.append(dict(id=rid,sha=sha,final=final,conclusion='success',passed=108,failed=0,skipped=0,captures=captures,artifact_id=artifact,artifact_sha256=digest))
log=(out/'build-and-review.log').read_text();advisories=sorted(set(re.findall(r'https://github.com/advisories/[^\s]+',log)))
(d/'evidence.json').write_text(json.dumps(dict(project='NAP 2.0',phase='P6.7',branch='codex/nap-2-fase-6-ux',base='e0ef903851b0679acf5c0263264f87471ff8df46',verified_source=code_sha,main_unchanged='5f81950275098881fd0befac0a13cde08c9d751f',historical_audit_date='2026-10-03',individual_records='synthetic_only',states=dict(conforming_simulation=28,documentation_review=10,unverifiable_without_originals=1),real_assets_used=False,production_operations=False,cutover=False,issue_47_49_modified=False,runs=runs,advisories=advisories,limitations=['Windows Server 2022 WPF offscreen renders; MSI Windows 11 acceptance pending','No original identity, metadata, image, integrity or production certification','Profile validates dimensions and Naming, not a complete role vocabulary'],stop='Human review required. No P6.8.'),indent=2,ensure_ascii=False)+'\n')
labels={'historical':'Inventario de 39 referencias','historical-children':'Children: metadata explícita farmer/village_child','historical-elder':'Elder: propuesta village_elder','historical-ambiguous':'Alternativas ambiguas: revisión documental','historical-duplicate':'Posible duplicado sintético de identidad','historical-unknown':'No verificable sin originales','historical-empty':'Filtro sin coincidencias','historical-context':'Cambio de universo operativo sin selección obsoleta','historical-export':'Exportación de simulación completa'}
pngs=sorted(out.glob('*.png'))
md=['# Galería WPF — P6.7','','Renderizados originales del árbol WPF en Windows Server 2022, con fixtures sintéticos. No son capturas del MSI ni certifican producción.','',f'Fuente verificada: `{code_sha}`. 34 PNG: 17 a 1440×900 y 17 a 1200×700. Vista superior, detalle documental e incidencias inferiores según escenario.','']
entries=[]
for p in pngs:
 name=p.stem.removeprefix('historico-');res=name[-8:] if name.endswith('1200x700') else name[-8:]
 mode=name[:-(len(res)+1)];section='Vista superior'
 for suffix,label in [('-detalle','Detalle documental'),('-incidencia','Evidencia y propuesta documental')]:
  if mode.endswith(suffix):mode=mode[:-len(suffix)];section=label;break
 caption=f'{labels.get(mode,mode)} · {section} · {res}'
 entries.append((p,caption));md += [f'## {caption}','',f'![{caption}](final/{p.name})','']
(d/'Galeria-WPF.md').write_text('\n'.join(md).rstrip()+'\n')
font='/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf';pdfmetrics.registerFont(TTFont('DejaVu',font));pdfmetrics.registerFont(TTFont('DejaVu-Bold','/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf'));pdfmetrics.registerFontFamily('DejaVu',normal='DejaVu',bold='DejaVu-Bold',italic='DejaVu',boldItalic='DejaVu-Bold')
styles=getSampleStyleSheet()
for k in ['Normal','Title','Heading1','Heading2']:styles[k].fontName='DejaVu'
styles['Normal'].fontSize=9;styles['Normal'].leading=13
styles['Title'].fontSize=20;styles['Title'].leading=25
styles['Heading1'].fontSize=14;styles['Heading1'].leading=19
styles['Heading2'].fontSize=11;styles['Heading2'].leading=15
for k in ['Title','Heading1','Heading2']:styles[k].textColor=colors.HexColor('#123e49')
def footer(canvas,doc):
 canvas.setFont('DejaVu',8);canvas.setFillColor(colors.HexColor('#47646b'));canvas.drawString(36,22,'NAP 2.0 · P6.7 · Simulación sintética · Revisión humana pendiente');canvas.drawRightString(doc.pagesize[0]-36,22,str(doc.page))
story=[]
for block in s.split('\n\n'):
 block=block.strip()
 if not block:continue
 sty=styles['Normal']
 if block.startswith('# '):block=block[2:];sty=styles['Title']
 elif block.startswith('## '):block=block[3:];sty=styles['Heading1']
 block=escape(block);block=re.sub(r'\*\*(.*?)\*\*',r'<b>\1</b>',block);block=re.sub(r'`(.*?)`',r'\1',block)
 block=re.sub(r'\[([^]]+)\]\((https?[^)]+)\)',r'<link href="\2" color="#225d70">\1</link>',block)
 block=re.sub(r'\[([^]]+)\]\(([^)]+)\)',r'\1 (\2)',block)
 story += [Paragraph(block.replace('\n',' '),sty),Spacer(1,9)]
SimpleDocTemplate(str(d/'Informe-revision.pdf'),pagesize=(595.28,841.89),leftMargin=42,rightMargin=42,topMargin=38,bottomMargin=40).build(story,onFirstPage=footer,onLaterPages=footer)
story=[]
for i,(p,caption) in enumerate(entries):
 if i:story.append(PageBreak())
 story += [Paragraph('Galería WPF · P6.7',styles['Title']),Paragraph(escape(caption),styles['Heading2']),Paragraph('Metadatos sintéticos · sin assets reales · sin homologación productiva',styles['Normal']),Spacer(1,12),Image(str(p),width=1000,height=1000*(700/1200 if '1200x700' in p.name else 900/1440)),Spacer(1,8),Paragraph(p.name,styles['Normal'])]
SimpleDocTemplate(str(d/'Galeria-WPF.pdf'),pagesize=(1190.55,841.89),leftMargin=36,rightMargin=36,topMargin=28,bottomMargin=34).build(story,onFirstPage=footer,onLaterPages=footer)
(d/'README.md').write_text(f'''# P6.7 — revisión humana pendiente

[Informe Markdown](Informe-revision.md) · [Informe PDF](Informe-revision.pdf) · [Galería PDF](Galeria-WPF.pdf) · [Galería Markdown](Galeria-WPF.md)

- [Inventario sintético documentado](Inventario-sintetico.md); [fixture JSON](../../src/NAP.Core/Fixtures/TreskalSyntheticInventory.json).
- [Simulación completa y anomalías](Informe-simulacion-anomalias.txt).
- [Auditoría previa](AUDIT.md); [homologación real futura, no ejecutada](Homologacion-real-futura.md).
- [TRX original final](final/phase6-historical.trx); [logs de build y revisión](final/build-and-review.log).
- [Manifiesto de procedencia](evidence.json); [hashes](SHA256SUMS.txt); [hashes de fuentes verificadas](verified-source-files.json).
- [Evidencias preliminares](preliminary/); [incidencias de transferencia](Transferencia-evidencia.md).

Código verificado `{code_sha}`. [Actions {run_id}](https://github.com/robdor80/NAP/actions/runs/{run_id}): dos builds Release, 108/108 pruebas focalizadas, 34 renderizados WPF a 1440×900 y 1200×700. No se ejecutan tests de volumen ZIP ni suites generales. Los commits posteriores de documentación no se atribuyen checks nuevos.

Verificación offline: `python Verify-evidence.py` (Python, Pillow). Renderizado reproducible en Windows con el ejecutable de esa fuente: `NAP.App.exe --phase6-preview=historical --phase6-output=<directorio-nuevo>`; añadir `--compact` para 1200×700. Los nueve modos y las comprobaciones de captura están en el workflow focalizado versionado. Se necesita el SDK/runtime Windows Desktop y la validación de licencia oficial vigente; no desactivar sus guardas.

Los 39 registros individuales son sintéticos. Solo los totales y las reglas citadas proceden del corte documental del 03/10/2026. Ningún asset se presenta como homologado para producción. Sin originales, adopción, CUTOVER, cambios en main, PR, fusión ni P6.8.
''')
# Preserve this generator with its exact observed artifact metadata for reproducibility.
if Path(__file__).resolve() != (d/'Generate-review.py').resolve(): shutil.copyfile(__file__,d/'Generate-review.py')
# Hash every delivery file, including original ZIPs and preliminary failures if any, except the digest list itself.
lines=[]
for p in sorted(d.rglob('*')):
 if p.is_file() and p.name!='SHA256SUMS.txt':lines.append(hashlib.sha256(p.read_bytes()).hexdigest()+'  '+p.relative_to(d).as_posix())
(d/'SHA256SUMS.txt').write_text('\n'.join(lines)+'\n')
print('Generated review, gallery and',len(lines),'file hashes.')

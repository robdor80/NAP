"""Render documents without changing any original WPF/Actions bytes. Run at repository root."""
from pathlib import Path
import re,json,hashlib,subprocess,collections,xml.etree.ElementTree as ET
from xml.sax.saxutils import escape
from PIL import Image as PILImage
from reportlab.platypus import SimpleDocTemplate,Paragraph,Spacer,Image,PageBreak,LongTable,TableStyle,KeepTogether
from reportlab.lib.styles import getSampleStyleSheet
from reportlab.lib import colors
from reportlab.lib.pagesizes import A4,landscape
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
D=Path('docs/phase6-acceptance'); cfg=json.loads((D/'C-Pruebas-evidencias/evidence.json').read_text());run=cfg['final_run'];sha=cfg['verified_source'];out=D/'runs'/str(run)
pdfmetrics.registerFont(TTFont('DejaVu','/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf'))
pdfmetrics.registerFont(TTFont('DejaVu-Bold','/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf'))
pdfmetrics.registerFontFamily('DejaVu',normal='DejaVu',bold='DejaVu-Bold',italic='DejaVu',boldItalic='DejaVu-Bold')
styles=getSampleStyleSheet()
for k in ['Normal','Title','Heading1','Heading2','Heading3']:
 styles[k].fontName='DejaVu';styles[k].wordWrap='CJK'
styles['Normal'].fontSize=9;styles['Normal'].leading=13
for k in ['Title','Heading1','Heading2','Heading3']:styles[k].textColor=colors.HexColor('#123e49')
styles['Title'].fontSize=20;styles['Title'].leading=25
styles['Heading1'].fontSize=14;styles['Heading1'].leading=19
styles['Heading2'].fontSize=11;styles['Heading2'].leading=15

def footer(canvas,doc):
 canvas.setFont('DejaVu',7);canvas.drawString(32,18,'NAP 2.0 · P6.8 · Aceptación humana pendiente');canvas.drawRightString(doc.pagesize[0]-32,18,str(doc.page))
def inline(s):
 s=escape(s);s=re.sub(r'\*\*(.*?)\*\*',r'<b>\1</b>',s);s=re.sub(r'\[([^\]]+)\]\((https?://[^)]+)\)', r'<link href="\2" color="#123e49">\1</link>',s);s=s.replace('`','')
 return s

def pdf_md(source,target):
 story=[];lines=Path(source).read_text().splitlines();i=0
 while i<len(lines):
  line=lines[i].strip();i+=1
  if not line:continue
  if line.startswith('|'):
   table=[]
   while True:
    if not re.fullmatch(r'[| :\-]+',line):table.append([Paragraph(inline(x.strip()),styles['Normal']) for x in line.strip('|').split('|')])
    if i>=len(lines) or not lines[i].strip().startswith('|'):break
    line=lines[i].strip();i+=1
   n=max(map(len,table));w=(A4[0]-64)/n
   t=LongTable(table,colWidths=[w]*n,repeatRows=1,hAlign='LEFT');t.setStyle(TableStyle([('BACKGROUND',(0,0),(-1,0),colors.HexColor('#e7f1f4')),('VALIGN',(0,0),(-1,-1),'TOP'),('GRID',(0,0),(-1,-1),.3,colors.HexColor('#aec7ce')),('BOTTOMPADDING',(0,0),(-1,-1),8)]));story += [t,Spacer(1,10)];continue
  h=re.match(r'^(#{1,4}) (.*)',line)
  style=styles['Title' if h and len(h[1])==1 else 'Heading1' if h and len(h[1])==2 else 'Heading2' if h else 'Normal']
  if h:line=h[2]
  elif not re.match(r'^(\d+\.|-) ',line):
   while i<len(lines) and lines[i].strip() and not re.match(r'^(#|\||\d+\.|-) ',lines[i]):line+=' '+lines[i].strip();i+=1
  story += [Paragraph(inline(line),style),Spacer(1,8)]
 SimpleDocTemplate(str(target),pagesize=A4,leftMargin=32,rightMargin=32,topMargin=32,bottomMargin=35,title=Path(source).stem,author='NAP 2.0').build(story,onFirstPage=footer,onLaterPages=footer)

pngs=sorted(out.glob('*.png'));entries=[]
labels={'active':'Inicio · actividad sintética','paused':'Inicio · universo pausado','review':'Inicio · incidencias y herramientas','offline':'Inicio · host desconectado','local':'Inicio · sin host','catalog':'Catálogo · sin assets','settings':'Ajustes · general','production':'Producción · trabajos durables de muestra','history':'Historial · resultado completado','choice':'Incidencia · opciones compatibles','exception':'Incidencia · propuesta y comparación','approved':'Incidencia · aprobación de muestra','blocked':'Incidencia · auditoría FAIL bloqueada','empty':'Producción · sin coincidencias','read-error':'Producción · error de conexión','manual':'Producción · acceso a operaciones manuales','activity':'Historial · página independiente de eventos','activity-decisions':'Historial · paginación de decisiones','diagnostics':'Diagnóstico · cuatro eventos ficticios','diagnostics-filtered':'Diagnóstico · filtro aplicado','diagnostics-export':'Diagnóstico · informe copiado y exportado','diagnostics-offline':'Diagnóstico · fuente local','diagnostics-error':'Diagnóstico · desconexión','licenses':'Licencias · fecha administrativa','licenses-90':'Licencias · umbral de 90 días','licenses-30':'Licencias · umbral de 30 días','licenses-7':'Licencias · umbral de 7 días','licenses-expired':'Licencias · vencimiento registrado','licenses-renewed':'Licencias · renovación ficticia','historical':'Histórico · inventario cerrado','historical-children':'Histórico · children y roles explícitos','historical-elder':'Histórico · elder y propuesta village_elder','historical-ambiguous':'Histórico · clasificación ambigua','historical-duplicate':'Histórico · duplicidad ficticia','historical-unknown':'Histórico · sin originales','historical-empty':'Histórico · filtro sin coincidencias','historical-context':'Histórico · cambio de universo operativo','historical-export':'Histórico · exportación real de texto sintético'}
md=['# Galería WPF — P6.8','',f'Fuente `{sha}` · [Actions {run}](https://github.com/robdor80/NAP/actions/runs/{run}).','',f'{len(pngs)} PNG originales. Árbol WPF real en Windows Server 2022, a 96 DPI y dos tamaños lógicos; no son capturas del MSI. Cada página muestra una vista, no una prueba funcional independiente. Las vistas adicionales corresponden al mismo escenario, con otra zona visible.','', 'Los modos de comparación registran zoom 1×/2× y hashes originales preservados. Los PNG con sidecar incluyen página, sección, selección, capacidades y hash; los estados repetidos sin cambio de píxeles se omiten. Los 38 escenarios se ejecutan sin host real, con dependencias de solo lectura y metadata ficticia.','']
for p in pngs:
 meta=p.with_name(p.name+'.json')
 if meta.exists():
  v=json.loads(meta.read_text());mode=v['mode'];view=v['view'] or 'principal';caption=f"{labels.get(mode,mode)} · {view} · {v['width']}×{v['height']}"
 else:
  caption='Comparación sintética · '+p.stem
 entries.append((p,caption));md += [f'## {caption}','',f'![{caption}](../runs/{run}/{p.name})','']
(D/'B-Capturas-WPF/Galeria-WPF.md').write_text('\n'.join(md).rstrip()+'\n')
story=[]
for idx,(p,caption) in enumerate(entries):
 if idx:story.append(PageBreak())
 story += [Paragraph(inline(caption),styles['Heading2']),Paragraph('Render sintético · '+sha[:12]+' · revisión humana pendiente',styles['Normal']),Spacer(1,8)]
 with PILImage.open(p) as im:w,h=im.size
 scale=min((landscape(A4)[0]-64)/w,(landscape(A4)[1]-115)/h)
 story.append(Image(str(p),width=w*scale,height=h*scale))
SimpleDocTemplate(str(D/'B-Capturas-WPF/Galeria-WPF.pdf'),pagesize=landscape(A4),leftMargin=32,rightMargin=32,topMargin=28,bottomMargin=32,title='NAP 2.0 P6.8 Galería WPF',author='NAP 2.0').build(story,onFirstPage=footer,onLaterPages=footer)
for source,target in [('A-Informe/Informe-ejecutivo.md','A-Informe/Informe-ejecutivo.pdf'),('D-Aceptacion-MSI/Guia-MSI.md','D-Aceptacion-MSI/Guia-MSI.pdf')]:pdf_md(D/source,D/target)
print('Generated documents and gallery:',len(pngs),'original PNGs')

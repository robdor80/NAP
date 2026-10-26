"""Render delivery documentation after verified Actions download; never executes NAP."""
from pathlib import Path
import json
import re
from xml.sax.saxutils import escape
from reportlab.platypus import SimpleDocTemplate, Paragraph, Spacer, Image, PageBreak
from reportlab.lib.styles import getSampleStyleSheet
from reportlab.lib import colors
from reportlab.lib.pagesizes import A4, landscape
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from PIL import Image as PILImage
D=Path(__file__).resolve().parent
R=D/'runs'/json.loads((D/'delivery.json').read_text())['runId']
for name,file in [('DejaVu','DejaVuSans.ttf'),('DejaVu-Bold','DejaVuSans-Bold.ttf')]:
 pdfmetrics.registerFont(TTFont(name,'/usr/share/fonts/truetype/dejavu/'+file))
pdfmetrics.registerFontFamily('DejaVu',normal='DejaVu',bold='DejaVu-Bold',italic='DejaVu',boldItalic='DejaVu-Bold')
styles=getSampleStyleSheet()
for name in ['Normal','Title','Heading1','Heading2']:
 styles[name].fontName='DejaVu';styles[name].wordWrap='CJK'
styles['Normal'].fontSize=9;styles['Normal'].leading=13
styles['Title'].fontSize=18;styles['Title'].leading=23
styles['Heading1'].fontSize=12;styles['Heading1'].leading=17
styles['Heading2'].fontSize=10;styles['Heading2'].leading=14
for name in ['Title','Heading1','Heading2']:styles[name].textColor=colors.HexColor('#123e49')
def footer(canvas,doc):
 canvas.setFont('DejaVu',7)
 canvas.drawString(32,18,'NAP 2.0 · Aceptación sintética · Prueba operativa y cierre de Fase 6 pendientes')
 canvas.drawRightString(doc.pagesize[0]-32,18,str(doc.page))
def inline(s):
 s=escape(s).replace('`','')
 s=re.sub(r'\*\*(.*?)\*\*',r'<b>\1</b>',s)
 s=re.sub(r'\[([^\]]+)\]\((https?://[^ )]+)\)',r'<link href="\2" color="#116779">\1</link>',s)
 s=re.sub(r'(?<!["\w>])(https?://[^\s<]+)',r'<link href="\1" color="#116779">\1</link>',s)
 return s
def render_md(name,screenshots=()):
 story=[]
 for line in (D/(name+'.md')).read_text().splitlines():
  line=line.strip()
  if not line:continue
  h=re.match(r'^(#{1,3}) (.*)',line)
  style=styles['Title' if h and len(h[1])==1 else 'Heading1' if h and len(h[1])==2 else 'Heading2' if h else 'Normal']
  if h:line=h[2]
  story.extend([Paragraph(inline(line),style),Spacer(1,8)])
 for file,label in screenshots:
  path=R/file
  story.extend([PageBreak(),Paragraph(label,styles['Heading1']),Paragraph('Captura WPF sintética del paquete verificado en Windows Server 2022; no corresponde al MSI.',styles['Normal']),Spacer(1,12)])
  with PILImage.open(path) as im:w,h=im.size
  scale=min((A4[0]-64)/w,(A4[1]-180)/h)
  story.append(Image(str(path),width=w*scale,height=h*scale))
 SimpleDocTemplate(str(D/(name+'.pdf')),pagesize=A4,leftMargin=32,rightMargin=32,topMargin=32,bottomMargin=36,title=name,author='NAP 2.0').build(story,onFirstPage=footer,onLaterPages=footer)
for n in ['Informe-ejecutivo','Informe-tecnico','Aislamiento','Dependencias-y-licencias']:
 render_md(n)
render_md('Guia-Windows11', [('Inicio.png','Inicio y navegación'),('Historico.png','Histórico sintético')])
story=[]
for idx,p in enumerate(sorted(R.glob('*.png'))):
 if idx:story.append(PageBreak())
 story.extend([Paragraph(p.stem,styles['Heading1']),Paragraph('Programa descargado y verificado · WPF · Windows Server 2022 · 0 errores de binding',styles['Normal']),Spacer(1,10)])
 with PILImage.open(p) as im:w,h=im.size
 scale=min((landscape(A4)[0]-64)/w,(landscape(A4)[1]-115)/h)
 story.append(Image(str(p),width=w*scale,height=h*scale))
SimpleDocTemplate(str(D/'Capturas-WPF.pdf'),pagesize=landscape(A4),leftMargin=32,rightMargin=32,topMargin=24,bottomMargin=30).build(story,onFirstPage=footer,onLaterPages=footer)

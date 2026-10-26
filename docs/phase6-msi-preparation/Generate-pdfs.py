"""Render documentary PDFs; does not run NAP or change original runner evidence."""
from pathlib import Path
import re
from xml.sax.saxutils import escape
from reportlab.platypus import SimpleDocTemplate, Paragraph, Spacer, Image, PageBreak, LongTable, TableStyle
from reportlab.lib.styles import getSampleStyleSheet
from reportlab.lib import colors
from reportlab.lib.pagesizes import A4, landscape
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from PIL import Image as PILImage
D=Path(__file__).resolve().parent
R=D/'runs/40123851505'
for name,file in [('DejaVu','DejaVuSans.ttf'),('DejaVu-Bold','DejaVuSans-Bold.ttf')]:
    pdfmetrics.registerFont(TTFont(name,'/usr/share/fonts/truetype/dejavu/'+file))
pdfmetrics.registerFontFamily('DejaVu',normal='DejaVu',bold='DejaVu-Bold',italic='DejaVu',boldItalic='DejaVu-Bold')
styles=getSampleStyleSheet()
for name in ['Normal','Title','Heading1','Heading2']:
    styles[name].fontName='DejaVu';styles[name].wordWrap='CJK'
styles['Normal'].fontSize=9;styles['Normal'].leading=13
styles['Title'].fontSize=19;styles['Title'].leading=24
styles['Heading1'].fontSize=13;styles['Heading1'].leading=18
styles['Heading2'].fontSize=11;styles['Heading2'].leading=15
for name in ['Title','Heading1','Heading2']:styles[name].textColor=colors.HexColor('#123e49')
def footer(canvas,doc):
    canvas.setFont('DejaVu',7)
    canvas.drawString(32,18,'NAP 2.0 · Fase 6 · Entrega del programa bloqueada · Aceptación humana pendiente')
    canvas.drawRightString(doc.pagesize[0]-32,18,str(doc.page))
def inline(s):
    s=escape(s).replace('`','')
    return re.sub(r'\*\*(.*?)\*\*',r'<b>\1</b>',s)
def render_md(name, screenshots=()):
    story=[];lines=(D/(name+'.md')).read_text().splitlines();i=0
    while i<len(lines):
        line=lines[i].strip();i+=1
        if not line:continue
        if line.startswith('|'):
            rows=[]
            while True:
                if not re.fullmatch(r'[| :\-]+',line):rows.append([Paragraph(inline(x.strip()),styles['Normal']) for x in line.strip('|').split('|')])
                if i>=len(lines) or not lines[i].strip().startswith('|'):break
                line=lines[i].strip();i+=1
            n=max(map(len,rows));widths=[(A4[0]-64)*.42,(A4[0]-64)*.58] if n==2 else [(A4[0]-64)/n]*n
            t=LongTable(rows,colWidths=widths,repeatRows=1)
            t.setStyle(TableStyle([('BACKGROUND',(0,0),(-1,0),colors.HexColor('#e7f1f4')),('VALIGN',(0,0),(-1,-1),'TOP'),('GRID',(0,0),(-1,-1),.3,colors.HexColor('#aec7ce')),('BOTTOMPADDING',(0,0),(-1,-1),7)]))
            story.extend([t,Spacer(1,10)]);continue
        h=re.match(r'^(#{1,3}) (.*)',line)
        style=styles['Title' if h and len(h[1])==1 else 'Heading1' if h and len(h[1])==2 else 'Heading2' if h else 'Normal']
        if h:line=h[2]
        elif not re.match(r'^(\d+\.|-) ',line):
            while i<len(lines) and lines[i].strip() and not re.match(r'^(#|\||\d+\.|-) ',lines[i]):line+=' '+lines[i].strip();i+=1
        story.extend([Paragraph(inline(line),style),Spacer(1,8)])
    for file,label in screenshots:
        story.extend([PageBreak(),Paragraph(label,styles['Heading1']),Paragraph('Captura sintética del runner · no es el MSI · código b2e9413f0d3b',styles['Normal']),Spacer(1,12)])
        with PILImage.open(R/file) as im:w,h=im.size
        scale=(A4[0]-64)/w
        story.append(Image(str(R/file),width=w*scale,height=h*scale))
    SimpleDocTemplate(str(D/(name+'.pdf')),pagesize=A4,leftMargin=32,rightMargin=32,topMargin=32,bottomMargin=36,title=name,author='NAP 2.0').build(story,onFirstPage=footer,onLaterPages=footer)
render_md('Informe-ejecutivo')
render_md('Informe-tecnico')
render_md('Guia-MSI',[
('inicio-local-1200x700.png','Inicio y las cinco secciones'),
('ajustes-diagnostics-offline-1200x700.png','Diagnóstico sin host'),
('ajustes-licenses-1200x700.png','Licencias: datos administrativos'),
('historico-historical-1200x700.png','Histórico: simulación cerrada')])
story=[]
for idx,p in enumerate(sorted(R.glob('*.png'))):
    if idx:story.append(PageBreak())
    story.extend([Paragraph(p.stem,styles['Heading2']),Paragraph('Render WPF sintético · Windows Server 2022 · 1200×700 · 0 errores de binding · no aceptación MSI',styles['Normal']),Spacer(1,10)])
    with PILImage.open(p) as im:w,h=im.size
    scale=min((landscape(A4)[0]-64)/w,(landscape(A4)[1]-100)/h)
    story.append(Image(str(p),width=w*scale,height=h*scale))
SimpleDocTemplate(str(D/'Galeria-WPF.pdf'),pagesize=landscape(A4),leftMargin=32,rightMargin=32,topMargin=25,bottomMargin=32,title='15 capturas WPF · preparación MSI').build(story,onFirstPage=footer,onLaterPages=footer)

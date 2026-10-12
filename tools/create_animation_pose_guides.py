#!/usr/bin/env python3
"""Create code-authored pose diagrams as ImageGen inputs, never repaint game art."""
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
DEST = ROOT/'assets/reference/pose-guides'
RUN_LEGS = [
    ((157,174),(176,214),(197,219),(105,161),(82,192),(96,195)),
    ((145,184),(150,218),(172,222),(113,157),(110,181),(130,184)),
    ((108,181),(98,218),(121,221),(140,165),(155,195),(177,197)),
    ((99,155),(74,177),(93,181),(159,160),(165,191),(184,195)),
    ((107,163),(82,189),(101,193),(157,174),(177,214),(197,219)),
    ((111,157),(110,181),(130,184),(145,184),(150,218),(172,222)),
    ((140,165),(155,195),(177,197),(108,181),(98,218),(121,221)),
    ((159,160),(165,191),(184,195),(99,155),(74,177),(93,181)),
]
KICK_LEGS = [
    ((145,174),(156,212),(174,220)),((142,170),(150,204),(169,208)),
    ((105,164),(80,180),(63,179)),((93,148),(80,164),(64,164)),
    ((165,168),(199,202),(217,204)),((175,151),(215,171),(234,175)),
    ((150,165),(155,200),(173,204)),((145,174),(156,212),(174,220)),
]


def main():
    DEST.mkdir(parents=True,exist_ok=True)
    for action in ['run','kick']:
        image=Image.new('RGB',(1024,512),'#F3F0E7')
        draw=ImageDraw.Draw(image)
        for i in range(8):
            ox,oy=i%4*256,i//4*256
            bob=([0,4,0,-4,0,4,0,-4][i] if action=='run' else 0)
            def point(p): return(p[0]+ox,p[1]+oy)
            def path(points,color,width=12):
                draw.line([point(p) for p in points],fill=color,width=width,joint='curve')
                for x,y in points:
                    draw.ellipse((ox+x-width/2,oy+y-width/2,ox+x+width/2,oy+y+width/2),fill=color)
            draw.rectangle((ox+1,oy+1,ox+255,oy+255),outline='#D5D6D0',width=1)
            draw.text((ox+12,oy+10),f'{action.upper()} {i}',fill='#132C3C')
            path([(40,229),(236,229)],'#CED4D5',2)
            hip=(128,132+bob)
            if action=='run':
                n1,n2,n3,f1,f2,f3=RUN_LEGS[i]
            else:
                n1,n2,n3=KICK_LEGS[i];f1,f2,f3=(135,174),(131,218),(153,222)
            path([hip,f1,f2,f3],'#806CB7')
            path([hip,n1,n2,n3],'#169DA1')
            # The body is deliberately a plain mannequin, only pose geometry is a reference.
            path([(128,132+bob),(127,89+bob)],'#132C3C',23)
            path([(118,91+bob),(140,92+bob)],'#132C3C',10)
            draw.ellipse((ox+125,oy+29+bob,ox+157,oy+62+bob),fill='#132C3C')
            draw.polygon([point((153,43+bob)),point((164,48+bob)),point((153,51+bob))],fill='#132C3C')
            front=i>=4 if action=='run' else i>=4 and i<=6
            na=[(118,91+bob),(150,114+bob),(164,98+bob)] if front else [(118,91+bob),(99,113+bob),(84,125+bob)]
            fa=[(140,92+bob),(104,111+bob),(91,121+bob)] if front else [(140,92+bob),(157,111+bob),(170,95+bob)]
            path(fa,'#806CB7',9);path(na,'#169DA1',9)
        image.save(DEST/f'{action}-8-frame-guide.png')
    image=Image.new('RGB',(1024,512),'#F3F0E7');draw=ImageDraw.Draw(image)
    directions=[('S',0,1),('SW',-.7,.7),('W',-1,0),('NW',-.7,-.7),('N',0,-1),('NE',.7,-.7),('E',1,0),('SE',.7,.7)]
    for i,(name,dx,dy) in enumerate(directions):
        ox,oy=i%4*256,i//4*256
        draw.rectangle((ox+1,oy+1,ox+255,oy+255),outline='#D5D6D0')
        draw.text((ox+16,oy+10),f'{i} / {name}',fill='#132C3C')
        draw.ellipse((ox+105,oy+46,ox+151,oy+92),fill='#169DA1' if i in [0,1,2,6,7] else '#806CB7')
        if i in [1,2,6,7]:
            side=-1 if i in [1,2] else 1
            draw.polygon([(ox+128+side*20,oy+64),(ox+128+side*31,oy+70),(ox+128+side*20,oy+75)],fill='#132C3C')
        draw.rounded_rectangle((ox+105,oy+96,ox+151,oy+166),radius=13,fill='#806CB7' if i in [3,4,5] else '#132C3C')
        draw.line([(ox+116,oy+166),(ox+111,oy+216)],fill='#132C3C',width=12)
        draw.line([(ox+140,oy+166),(ox+145,oy+216)],fill='#132C3C',width=12)
        start=(ox+128,oy+238);end=(ox+128+dx*43,oy+238+dy*12)
        draw.line([start,end],fill='#DC743E',width=5)
        draw.ellipse((end[0]-4,end[1]-4,end[0]+4,end[1]+4),fill='#DC743E')
        if i in [0,1,7]: draw.text((ox+170,oy+65),'FACE',fill='#132C3C')
        elif i in [3,4,5]: draw.text((ox+166,oy+65),'BACK',fill='#132C3C')
        else: draw.text((ox+166,oy+65),'PROFILE',fill='#132C3C')
    image.save(DEST/'turn-8-direction-guide.png')
    print('Created run, kick and turn diagrams (technical references only)')


if __name__=='__main__':
    main()

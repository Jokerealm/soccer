#!/usr/bin/env python3
"""Draw technical joint references for a 14-frame alternating run; not game art."""
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
PHASES = ['contact', 'compress', 'support', 'pass', 'push', 'flight', 'reach']
# Near/far knee, ankle and toe. Opposite half swaps the anatomical legs, not yaw.
STANCE = [
    [(178,218),(195,266),(222,271)],
    [(175,225),(178,269),(206,271)],
    [(160,228),(155,269),(184,271)],
    [(145,227),(132,265),(156,271)],
    [(125,227),(109,262),(130,271)],
    [(103,208),(92,237),(111,242)],
    [(112,191),(101,233),(125,237)],
]
SWING = [
    [(112,205),(105,245),(125,250)],
    [(104,193),(83,210),(105,214)],
    [(126,194),(102,221),(126,225)],
    [(162,195),(144,231),(165,232)],
    [(195,197),(177,236),(196,237)],
    [(199,200),(222,249),(244,251)],
    [(198,220),(213,266),(239,270)],
]


def main():
    image = Image.new('RGB',(1280,1280),'#f5f1e7')
    draw = ImageDraw.Draw(image)
    for i in range(14):
        ox,oy = i%4*320,i//4*320
        k=i%7;bob=[0,7,5,3,-1,-5,-1][k]
        hip=(148,170+bob)
        near,far=(STANCE[k],SWING[k]) if i<7 else (SWING[k],STANCE[k])
        def p(point):return(ox+point[0],oy+point[1])
        def limb(points,color,width=13):
            draw.line([p(pt) for pt in points],fill=color,width=width,joint='curve')
            for x,y in points:
                draw.ellipse((ox+x-width/2,oy+y-width/2,ox+x+width/2,oy+y+width/2),fill=color)
        draw.rectangle((ox+1,oy+1,ox+319,oy+319),outline='#d7d8cd')
        draw.text((ox+14,oy+12),f'{i:02d} / {PHASES[k]} '+('A' if i<7 else 'B'),fill='#132c3c')
        draw.line([p((35,282)),p((285,282))],fill='#adbcbc',width=2)
        limb([hip]+far,'#9b6fba')
        limb([hip]+near,'#169da1')
        limb([hip,(166,108+bob)],'#132c3c',25)
        draw.ellipse((ox+163,oy+45+bob,ox+203,oy+88+bob),fill='#132c3c')
        draw.polygon([p((200,61+bob)),p((214,68+bob)),p((200,75+bob))],fill='#132c3c')
        # Near arm swings against the near leg; interpolates across each half.
        t=k/6 if i<7 else 1-k/6
        near_arm=[(159,113+bob),(115+72*t,143+bob),(95+105*t,127+25*(1-t)+bob)]
        far_arm=[(170,116+bob),(195-65*t,142+bob),(218-106*t,120+25*t+bob)]
        limb(far_arm,'#9b6fba',10);limb(near_arm,'#169da1',10)
        draw.text((ox+40,oy+294),'NEAR = CYAN / FAR = PURPLE',fill='#557273')
    # Last two cells are deliberately empty, not duplicate loop-closing poses.
    for i in [14,15]:
        ox,oy=i%4*320,i//4*320
        draw.rectangle((ox+1,oy+1,ox+319,oy+319),outline='#d7d8cd')
        draw.text((ox+114,oy+150),'EMPTY',fill='#9aaba8')
    dest=ROOT/'assets/reference/pose-guides/run-14-frame-guide-v2.png'
    image.save(dest)
    print(dest.relative_to(ROOT))


if __name__=='__main__':
    main()

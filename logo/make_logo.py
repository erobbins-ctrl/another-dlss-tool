from PIL import Image, ImageDraw, ImageFilter
import numpy as np, math
S=1024; SS=4; N=S*SS
yy,xx=np.mgrid[0:N,0:N].astype(np.float32)
t=np.clip((xx*0.6+yy*1.0)/(1.6*N),0,1)[...,None]
c0=np.array([70,190,122],np.float32); c1=np.array([12,58,40],np.float32)
rgb=(c0*(1-t)+c1*t).astype(np.uint8)
bg=Image.fromarray(rgb,'RGB')
mask=Image.new('L',(N,N),0); ImageDraw.Draw(mask).rounded_rectangle([0,0,N-1,N-1],radius=int(N*0.225),fill=255)
logo=Image.new('RGBA',(N,N),(0,0,0,0)); logo.paste(bg,(0,0),mask)
# soft glow behind the mark
glow=Image.new('L',(N,N),0); ImageDraw.Draw(glow).ellipse([0.22*N,0.24*N,0.78*N,0.80*N],fill=70)
glow=glow.filter(ImageFilter.GaussianBlur(N*0.08))
gl=Image.new('RGBA',(N,N),(190,255,215,0)); gl.putalpha(Image.composite(glow,Image.new('L',(N,N),0),mask))
logo=Image.alpha_composite(logo,gl)
d=ImageDraw.Draw(logo)
W=(255,255,255,255)
# the mark: a ring that is pixelated on the left and smooth on the right (low-res in, clean out)
cx,cy=0.48*N,0.53*N; R=0.255*N; T=0.105*N
d.arc([cx-R-T/2,cy-R-T/2,cx+R+T/2,cy+R+T/2],start=-90,end=90,fill=W,width=int(T))
g=T*0.92
for i in range(-12,13):
    for j in range(-12,13):
        x=cx+(i+0.5)*g-g/2; y=cy+(j+0.5)*g-g/2
        mx,my=x+g/2,y+g/2
        dist=math.hypot(mx-cx,my-cy)
        if abs(dist-R) <= T*0.55 and mx < cx-g*0.15:
            d.rounded_rectangle([x+g*0.05,y+g*0.05,x+g*0.95,y+g*0.95],radius=g*0.14,fill=W)
# sparkles: the neural touch
def star(sx,sy,r1,r2,fill):
    pts=[]
    for k in range(8):
        a=math.radians(90+k*45); r=r1 if k%2==0 else r2
        pts.append((sx+r*math.cos(a), sy-r*math.sin(a)))
    d.polygon(pts,fill=fill)
star(0.79*N,0.23*N,0.115*N,0.028*N,W)
star(0.88*N,0.37*N,0.05*N,0.013*N,(255,255,255,220))
out=logo.resize((S,S),Image.LANCZOS)
out.save('logo_1024.png')
out.resize((256,256),Image.LANCZOS).save('logo_256.png')
out.resize((128,128),Image.LANCZOS).save('logo_128.png')
out.save('app.ico',sizes=[(s,s) for s in [16,24,32,48,64,128,256]])
print('ok')

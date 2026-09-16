using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

// Deterministic local preparation of user-approved generated sprite sheets.
// Only removes connected neutral checkerboard regions; no character repainting.
public static class PrepareSprites {
    public static int Main(string[] args) {
        if(args.Length<2)return 2;
        using(var source=new Bitmap(args[0])) using(var image=new Bitmap(source.Width,source.Height,PixelFormat.Format32bppArgb)) {
            using(var g=Graphics.FromImage(image))g.DrawImageUnscaled(source,0,0);
            int w=image.Width,h=image.Height,n=w*h;
            var data=image.LockBits(new Rectangle(0,0,w,h),ImageLockMode.ReadWrite,PixelFormat.Format32bppArgb);
            byte[] rgba=new byte[data.Stride*h]; Marshal.Copy(data.Scan0,rgba,0,rgba.Length);
            bool[] candidate=new bool[n],seen=new bool[n],removed=new bool[n];
            for(int i=0;i<n;i++) {
                int b=rgba[i*4],g=rgba[i*4+1],r=rgba[i*4+2];
                int min=Math.Min(r,Math.Min(g,b)),max=Math.Max(r,Math.Max(g,b));
                candidate[i]=max-min<=22 && min>=88;
            }
            var queue=new int[n];
            for(int seed=0;seed<n;seed++) {
                if(!candidate[seed]||seen[seed])continue;
                int head=0,tail=1,minLight=255,maxLight=0,jumps=0; bool edge=false;
                queue[0]=seed;seen[seed]=true;
                while(head<tail) {
                    int p=queue[head++],x=p%w,y=p/w,light=rgba[p*4+1];
                    minLight=Math.Min(minLight,light);maxLight=Math.Max(maxLight,light);
                    if(x==0||y==0||x==w-1||y==h-1)edge=true;
                    if(x>0)Visit(p-1,p,candidate,seen,queue,ref tail,rgba,ref jumps);
                    if(x<w-1)Visit(p+1,p,candidate,seen,queue,ref tail,rgba,ref jumps);
                    if(y>0)Visit(p-w,p,candidate,seen,queue,ref tail,rgba,ref jumps);
                    if(y<h-1)Visit(p+w,p,candidate,seen,queue,ref tail,rgba,ref jumps);
                }
                // Never infer background from an enclosed pale component: face,
                // bangs, and white clothing can also have checker-like contrast.
                if(edge) for(int k=0;k<tail;k++)removed[queue[k]]=true;
            }
            // Remove a neutral one-pixel antialias fringe only along known background.
            var grown=(bool[])removed.Clone();
            for(int y=1;y<h-1;y++)for(int x=1;x<w-1;x++) {
                int p=y*w+x;if(removed[p])continue;
                int b=rgba[p*4],g=rgba[p*4+1],r=rgba[p*4+2];
                if(Math.Max(r,Math.Max(g,b))-Math.Min(r,Math.Min(g,b))>30||Math.Min(r,Math.Min(g,b))<105)continue;
                int neighbors=0;for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)if(removed[p+dy*w+dx])neighbors++;
                if(neighbors>=5)grown[p]=true;
            }
            var foreground=new bool[n];for(int i=0;i<n;i++)foreground[i]=!grown[i];Array.Clear(seen,0,seen.Length);
            for(int seed=0;seed<n;seed++) {
                if(!foreground[seed]||seen[seed])continue;
                int head=0,tail=1,unused=0;queue[0]=seed;seen[seed]=true;
                while(head<tail) { int p=queue[head++],x=p%w,y=p/w;
                    if(x>0)Visit(p-1,p,foreground,seen,queue,ref tail,rgba,ref unused);
                    if(x<w-1)Visit(p+1,p,foreground,seen,queue,ref tail,rgba,ref unused);
                    if(y>0)Visit(p-w,p,foreground,seen,queue,ref tail,rgba,ref unused);
                    if(y<h-1)Visit(p+w,p,foreground,seen,queue,ref tail,rgba,ref unused);
                }
                if(tail<32)for(int i=0;i<tail;i++)grown[queue[i]]=true;
            }
            for(int i=0;i<n;i++)if(grown[i])rgba[i*4+3]=0;
            Marshal.Copy(rgba,0,data.Scan0,rgba.Length);image.UnlockBits(data);
            string directory=Path.GetDirectoryName(Path.GetFullPath(args[1]));Directory.CreateDirectory(directory);
            // Use the observed row boundary, then register all frames on a common
            // canvas with a shared scale and foot anchor. Dragged poses keep head
            // height, so bent knees remain visibly suspended.
            int split=args.Length>2?Int32.Parse(args[2]):h/2;
            var bounds=new Rectangle[6]; int maxHeight=0;
            for(int cell=0;cell<6;cell++) {
                int x0=(cell%3)*w/3,x1=(cell%3+1)*w/3,y0=cell<3?0:split,y1=cell<3?split:h;
                int left=x1,right=x0,top=y1,bottom=y0;
                for(int yy=y0;yy<y1;yy++)for(int xx=x0;xx<x1;xx++)if(rgba[(yy*w+xx)*4+3]>20) { left=Math.Min(left,xx);right=Math.Max(right,xx);top=Math.Min(top,yy);bottom=Math.Max(bottom,yy); }
                bounds[cell]=Rectangle.FromLTRB(left,top,right+1,bottom+1);maxHeight=Math.Max(maxHeight,bottom-top+1);
            }
            using(var normalized=new Bitmap(1254,1254,PixelFormat.Format32bppArgb))using(var g=Graphics.FromImage(normalized)) {
                g.CompositingMode=System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                double factor=584.0/maxHeight;
                bool reactions=Path.GetFileNameWithoutExtension(args[1])=="reactions";
                for(int cell=0;cell<6;cell++) {
                    var b=bounds[cell];float dw=(float)(b.Width*factor),dh=(float)(b.Height*factor);
                    float x=(cell%3)*418+(418-dw)/2,y=(cell/3)*627+610-dh;
                    if(reactions&&cell>=4)y=(cell/3)*627+26;
                    g.DrawImage(image,new RectangleF(x,y,dw,dh),b,GraphicsUnit.Pixel);
                }
                normalized.Save(args[1],ImageFormat.Png);
            }
            int clear=0;foreach(bool b in grown)if(b)clear++;
            Console.WriteLine(Path.GetFileName(args[1])+": "+w+"x"+h+", transparent "+(100.0*clear/n).ToString("F1")+"%");
        }
        return 0;
    }
    static void Visit(int q,int p,bool[] candidate,bool[] seen,int[] queue,ref int tail,byte[] rgba,ref int jumps) {
        if(!candidate[q])return;
        if(Math.Abs(rgba[q*4+1]-rgba[p*4+1])>25)jumps++;
        if(seen[q])return;seen[q]=true;queue[tail++]=q;
    }
}

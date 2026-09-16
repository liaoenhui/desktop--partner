using System;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Collections.Generic;
class BuildPetIcon {
    static void Main(string[] args) {
        using(var source=new Bitmap(args[0])) {
            float w=source.Width/3f,h=source.Height/2f;
            var crop=new RectangleF(w*.08f,h*.015f,w*.84f,h*.56f);
            int[] sizes={16,24,32,48,64,128,256};var images=new List<byte[]>();
            foreach(int size in sizes)using(var bitmap=new Bitmap(size,size,PixelFormat.Format32bppArgb)) {
                using(var g=Graphics.FromImage(bitmap)) {
                    g.SmoothingMode=SmoothingMode.AntiAlias;g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.PixelOffsetMode=PixelOffsetMode.HighQuality;
                    using(var brush=new SolidBrush(Color.FromArgb(255,39,27,70)))g.FillEllipse(brush,0,0,size-1,size-1);
                    g.DrawImage(source,new RectangleF(0,0,size,size),crop,GraphicsUnit.Pixel);
                }
                using(var memory=new MemoryStream()) { bitmap.Save(memory,ImageFormat.Png);images.Add(memory.ToArray()); }
                if(size==256)bitmap.Save(args[2],ImageFormat.Png);
            }
            using(var writer=new BinaryWriter(File.Create(args[1]))) {
                writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)sizes.Length);int offset=6+16*sizes.Length;
                for(int i=0;i<sizes.Length;i++) { writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((byte)0);writer.Write((byte)0);writer.Write((ushort)1);writer.Write((ushort)32);writer.Write(images[i].Length);writer.Write(offset);offset+=images[i].Length; }
                foreach(var image in images)writer.Write(image);
            }
        }
    }
}

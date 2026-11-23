using System;using System.IO;using System.Drawing;using System.Drawing.Drawing2D;using System.Drawing.Imaging;using System.Collections.Generic;
public static class IconBuild{
 static GraphicsPath Round(float x,float y,float w,float h,float radius){var p=new GraphicsPath();float d=radius*2;p.AddArc(x,y,d,d,180,90);p.AddArc(x+w-d,y,d,d,270,90);p.AddArc(x+w-d,y+h-d,d,d,0,90);p.AddArc(x,y+h-d,d,d,90,90);p.CloseFigure();return p;}
 public static void Main(string[] args){var frames=new List<byte[]>();int[] sizes={16,24,32,48,64,128,256};foreach(int size in sizes)using(var b=new Bitmap(size,size,PixelFormat.Format32bppArgb)){
 using(var g=Graphics.FromImage(b)){g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;g.ScaleTransform(size/256f,size/256f);
 using(var fill=new SolidBrush(Color.FromArgb(0x3A,0x57,0xC4)))using(var p=Round(12,12,232,190,58)){g.FillPath(fill,p);g.FillPolygon(fill,new[]{new PointF(54,186),new PointF(54,244),new PointF(114,190)});}
 using(var pen=new Pen(Color.White,20){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round})g.DrawLines(pen,new[]{new PointF(87,151),new PointF(87,65),new PointF(169,151),new PointF(169,65)});}
 using(var m=new MemoryStream()){b.Save(m,ImageFormat.Png);frames.Add(m.ToArray());}}
 using(var f=File.Create(args[0]))using(var w=new BinaryWriter(f)){w.Write((ushort)0);w.Write((ushort)1);w.Write((ushort)sizes.Length);int offset=6+16*sizes.Length;for(int i=0;i<sizes.Length;i++){w.Write((byte)(sizes[i]==256?0:sizes[i]));w.Write((byte)(sizes[i]==256?0:sizes[i]));w.Write((byte)0);w.Write((byte)0);w.Write((ushort)1);w.Write((ushort)32);w.Write(frames[i].Length);w.Write(offset);offset+=frames[i].Length;}foreach(var bytes in frames)w.Write(bytes);}}
}
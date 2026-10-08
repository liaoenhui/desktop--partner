using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SilverWolfPet {
public sealed class EdgeClipSpec {public string[] frames;public int[] milliseconds;}
public sealed class EdgeLibrarySpec {public Dictionary<string,string> frames;public Dictionary<string,EdgeClipSpec> clips;}
public partial class PetWindow {
    readonly Dictionary<string,BitmapSource> edgeImages=new Dictionary<string,BitmapSource>();
    readonly Dictionary<string,AnimationClip> edgeClips=new Dictionary<string,AnimationClip>();
    string edgeAction;
    double edgeActionStart,nextEdgeBlink,nextEdgeMood,edgeHappyAfter;
    void LoadEdgeAnimations(string root) {
        string folder=Path.Combine(root,"assets","edge-mode");
        var spec=new JavaScriptSerializer().Deserialize<EdgeLibrarySpec>(File.ReadAllText(Path.Combine(folder,"library.json")));
        if(spec==null||spec.frames==null||spec.clips==null||spec.frames.Count>16||spec.clips.Count>12)throw new InvalidDataException("Invalid edge library");
        edgeImages["idle"]=edgeFrame;
        foreach(var frame in spec.frames.Where(x=>x.Key!="idle")) {
            string path=Path.GetFullPath(Path.Combine(folder,frame.Value));
            if(!path.StartsWith(Path.GetFullPath(folder)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Edge frame outside library");
            var source=ReadEdgeImage(path);
            // Register each variant to the same fixed canvas. The pose and quota
            // attachment keep identical dimensions across all animation frames.
            var visual=new DrawingVisual();using(var d=visual.RenderOpen())d.DrawImage(source,new Rect(0,0,edgeFrame.PixelWidth,edgeFrame.PixelHeight));
            var image=new RenderTargetBitmap(edgeFrame.PixelWidth,edgeFrame.PixelHeight,96,96,PixelFormats.Pbgra32);image.Render(visual);image.Freeze();edgeImages[frame.Key]=image;
        }
        foreach(var item in spec.clips) {
            var clip=item.Value;
            if(clip==null||clip.frames==null||clip.milliseconds==null||clip.frames.Length==0||clip.frames.Length>16||clip.frames.Length!=clip.milliseconds.Length||clip.frames.Any(x=>!edgeImages.ContainsKey(x))||clip.milliseconds.Any(x=>x<50||x>4000))throw new InvalidDataException("Invalid edge animation timing");
            edgeClips[item.Key]=new AnimationClip {Frames=clip.frames.Select(x=>edgeImages[x]).ToArray(),Milliseconds=clip.milliseconds,Loop=false};
        }
        if(new[]{"blink","happy","sleepy"}.Any(x=>!edgeClips.ContainsKey(x)))throw new InvalidDataException("Missing edge animation");
    }
    void ResetEdgeAnimation(double now) {
        edgeAction=null;SetFrame(edgeFrame);previous.Source=null;previous.Opacity=0;pet.Opacity=1;shift.Y=0;
        nextEdgeBlink=now+3.5+random.NextDouble()*3.5;nextEdgeMood=now+45+random.NextDouble()*40;
        timer.Interval=TimeSpan.FromMilliseconds(200);
    }
    void PlayEdgeAnimation(string action,double now) {
        if(!edgeMode||!edgeClips.ContainsKey(action)||pressed)return;
        if(action=="happy"&&now<edgeHappyAfter)return;
        if(action=="happy")edgeHappyAfter=now+4;
        edgeAction=action;edgeActionStart=now;timer.Interval=TimeSpan.FromMilliseconds(50);
        SetFrame(edgeClips[action].Frames[0]);previous.Opacity=0;pet.Opacity=1;
    }
    void AdvanceEdgeAnimation(double now,double idleSeconds) {
        if(!edgeMode)return;
        if(pressed||menuOpen){if(edgeAction!=null)ResetEdgeAnimation(now);return;}
        if(edgeAction==null) {
            if(!prefs.Idle)return;
            if(idleSeconds>=90&&now>=nextEdgeMood){PlayEdgeAnimation("sleepy",now);return;}
            if(now>=nextEdgeBlink){PlayEdgeAnimation("blink",now);return;}
            return;
        }
        var clip=edgeClips[edgeAction];double age=now-edgeActionStart;
        if(age>=clip.Duration) {
            string ended=edgeAction;double mood=nextEdgeMood;ResetEdgeAnimation(now);
            if(ended=="blink")nextEdgeMood=mood;
            return;
        }
        SetFrame(clip.At(age));previous.Opacity=0;pet.Opacity=1;
        // Subpixel breath/nod only; hands stay on the edge, no canvas resizing.
        shift.Y=edgeAction=="sleepy"?.45*(1-Math.Cos(age*3))*.5:0;
    }
    void EdgeAnimationTests(string root) {
        bool oldIdle=prefs.Idle;prefs.Idle=true;var tasks=codexTasks;codexTasks=null;
        EnterEdgeMode(false,System.Windows.Forms.Screen.PrimaryScreen.DeviceName,180);
        var anchor=EdgeBeanAnchor();double now=elapsed.Elapsed.TotalSeconds;
        nextEdgeBlink=now;AdvanceEdgeAnimation(now,0);
        if(edgeAction!="blink"||cachedBitmap!=edgeImages["blink"])throw new Exception("edge idle blink not triggered");
        AdvanceEdgeAnimation(now+1,0);if(edgeAction!=null||cachedBitmap!=edgeFrame)throw new Exception("edge blink did not finish");
        edgeHappyAfter=0;PlayEdgeAnimation("happy",now+2);AdvanceEdgeAnimation(now+2.2,0);
        if(cachedBitmap!=edgeImages["happy"])throw new Exception("edge happy expression missing");
        AdvanceEdgeAnimation(now+4,0);nextEdgeMood=now+5;nextEdgeBlink=now+50;
        AdvanceEdgeAnimation(now+5,120);if(edgeAction!="sleepy")throw new Exception("idle edge doze missing");
        if(EdgeBeanAnchor()!=anchor||Width!=EdgeWidth||Height!=EdgeHeight)throw new Exception("edge animation moved its quota anchor or resized");
        pressed=true;AdvanceEdgeAnimation(now+6,120);pressed=false;
        if(edgeAction!=null||cachedBitmap!=edgeFrame||shift.Y!=0)throw new Exception("drag did not cancel edge animation");
        prefs.Idle=false;nextEdgeBlink=0;AdvanceEdgeAnimation(now+20,120);if(edgeAction!=null)throw new Exception("edge animation ignored idle preference");
        foreach(var name in new[]{"blink","happy","sleepy"})RenderEdgeAnimationPreview(root,name);
        ExitEdgeMode();codexTasks=tasks;prefs.Idle=oldIdle;
    }
    void RenderEdgeAnimationPreview(string root,string name) {
        var clip=edgeClips[name];var encoder=new GifBitmapEncoder();
        var frames=new[]{edgeFrame}.Concat(clip.Frames).ToArray();
        var times=new[]{900}.Concat(clip.Milliseconds).ToArray();
        for(int i=0;i<frames.Length;i++) {
            var scene=new DrawingVisual();using(var d=scene.RenderOpen()) {
                d.DrawRectangle(QuotaPanelChrome.Solid("#1A2339"),null,new Rect(0,0,168,288));
                d.DrawImage(frames[i],new Rect(0,84,168,198));
                var bean=new EdgeMochi {Width=36,Height=28};bean.Measure(new Size(36,28));bean.Arrange(new Rect(0,0,36,28));
                d.DrawRectangle(new VisualBrush(bean),null,new Rect(78,60,72,56));
            }
            var bitmap=new RenderTargetBitmap(168,288,96,96,PixelFormats.Pbgra32);bitmap.Render(scene);
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
        }
        WriteTimedEdgeGif(encoder,Path.Combine(root,"qa","edge-"+name+".gif"),times);
    }
    // WIC drops frame-delay metadata when encoding GIFs on some Windows builds.
    // Add standard timing/loop records without modifying encoded frame pixels.
    static void WriteTimedEdgeGif(GifBitmapEncoder encoder,string path,int[] times) {
        byte[] data;using(var stream=new MemoryStream()){encoder.Save(stream);data=stream.ToArray();}
        int pos=13+((data[10]&128)!=0?3*(1<<((data[10]&7)+1)):0),frame=0;
        var output=new List<byte>(data.Take(pos));
        output.AddRange(new byte[]{33,255,11});output.AddRange(System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0"));output.AddRange(new byte[]{3,1,0,0,0});
        while(pos<data.Length&&data[pos]!=59) {
            int end;
            if(data[pos]==33) {
                end=pos+2;while(data[end]!=0)end+=1+data[end];end++;
                if(data[pos+1]!=249&&data[pos+1]!=255)output.AddRange(data.Skip(pos).Take(end-pos));
            } else if(data[pos]==44) {
                int delay=Math.Max(5,times[frame++]/10);
                output.AddRange(new byte[]{33,249,4,8,(byte)(delay&255),(byte)(delay>>8),0,0});
                end=pos+10+((data[pos+9]&128)!=0?3*(1<<((data[pos+9]&7)+1)):0);end++;
                while(data[end]!=0)end+=1+data[end];end++;
                output.AddRange(data.Skip(pos).Take(end-pos));
            } else throw new InvalidDataException("Unexpected GIF record");
            pos=end;
        }
        if(frame!=times.Length)throw new InvalidDataException("GIF frame count mismatch");
        output.Add(59);File.WriteAllBytes(path,output.ToArray());
    }
}
}

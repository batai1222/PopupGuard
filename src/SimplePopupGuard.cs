using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("弹窗拦截")]
[assembly: System.Reflection.AssemblyDescription("简洁的本地弹窗拦截与窗口记录")]
[assembly: System.Reflection.AssemblyVersion("1.0.4.0")]

namespace SimplePopupGuard
{
    internal static class Native
    {
        internal delegate bool EnumProc(IntPtr hwnd, IntPtr parameter);
        internal delegate void EventProc(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time);
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; public Rectangle Box { get { return Rectangle.FromLTRB(Left,Top,Right,Bottom); } } }
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc callback, IntPtr parameter);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr hwnd, uint command);
        [DllImport("user32.dll", EntryPoint="GetWindowLongW")] internal static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern int GetClassName(IntPtr hwnd, StringBuilder text, int count);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
        [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out Rect rect, int size);
        [DllImport("dwmapi.dll", EntryPoint="DwmGetWindowAttribute")] internal static extern int DwmGetWindowInt(IntPtr hwnd, int attribute, out int value, int size);
        [DllImport("user32.dll", SetLastError=true)] internal static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, EventProc callback, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll", SetLastError=true)] internal static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError=true)] internal static extern bool ShowWindowAsync(IntPtr hwnd, int command);
        [DllImport("kernel32.dll", SetLastError=true)] internal static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] internal static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref uint size);
        [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr handle);
        [DllImport("user32.dll")] internal static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
        internal static Rectangle Desktop { get { return new Rectangle(GetSystemMetrics(76),GetSystemMetrics(77),GetSystemMetrics(78),GetSystemMetrics(79)); } }
        internal static string PathFor(uint pid)
        {
            IntPtr p = OpenProcess(0x1000,false,pid); if(p==IntPtr.Zero) return "";
            try { uint n=32768; StringBuilder b=new StringBuilder((int)n); return QueryFullProcessImageName(p,0,b,ref n) ? b.ToString() : ""; }
            finally { CloseHandle(p); }
        }
        internal static bool Protected(string path, string cls, uint pid)
        {
            if(pid==(uint)Process.GetCurrentProcess().Id || String.IsNullOrEmpty(path)) return true;
            string name=System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            string[] names={"avp","avpui","hipsdaemon","popblock","winlogon","logonui","lsass","consent","credentialuibroker","msmpeng","securityhealthservice","securityhealthsystray","sechealthui","keepass","keepassxc","1password","bitwarden","codex","chatgpt","dwm","sihost","shellexperiencehost","startmenuexperiencehost","textinputhost","explorer"};
            return names.Contains(name) || cls=="Progman" || cls=="WorkerW" || cls=="Shell_TrayWnd" || cls=="Shell_SecondaryTrayWnd";
        }
        internal static WindowInfo Read(IntPtr hwnd, bool requireVisible){return Read(hwnd,requireVisible,false);}
        internal static WindowInfo Read(IntPtr hwnd, bool requireVisible,bool includeObstructions)
        {
            if(hwnd==IntPtr.Zero || !IsWindow(hwnd) || GetAncestor(hwnd,2)!=hwnd) return null;
            if(requireVisible && (!IsWindowVisible(hwnd) || IsIconic(hwnd))) return null;
            int cloaked; if(DwmGetWindowInt(hwnd,14,out cloaked,4)==0 && cloaked!=0) return null;
            uint pid; uint tid=GetWindowThreadProcessId(hwnd,out pid);
            StringBuilder title=new StringBuilder(1024),cls=new StringBuilder(256);
            GetClassName(hwnd,cls,cls.Capacity);
            if(cls.ToString()=="CodexComputerUseCursorOverlay")return null;
            string path=PathFor(pid); bool selectable=!Protected(path,cls.ToString(),pid);if(!selectable&&!includeObstructions)return null;
            string processName=System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            if(processName=="nvidia overlay"||processName=="gamebar"||processName=="gamebarftserver"||processName=="gamebarpresencewriter")return null;
            if(processName=="codex-computer-use")
            {
                if(!includeObstructions)return null;
                StringBuilder helperTitle=new StringBuilder(200);GetWindowText(hwnd,helperTitle,helperTitle.Capacity);
                if(helperTitle.ToString()=="Codex Computer Use Cursor Overlay")return null;
                selectable=false;
            }
            if(selectable)GetWindowText(hwnd,title,title.Capacity);
            Rect r; if(!GetWindowRect(hwnd,out r)) return null;
            Rect visual; if(DwmGetWindowAttribute(hwnd,9,out visual,Marshal.SizeOf(typeof(Rect)))==0) r=visual;
            if(r.Box.Width<40 || r.Box.Height<25 || r.Box.Width>12000 || r.Box.Height>8000) return null;
            int kind=(GetWindow(hwnd,4)!=IntPtr.Zero?1:0)|((GetWindowLong(hwnd,-20)&0x80)!=0?2:0);
            return new WindowInfo{Handle=hwnd,Pid=pid,ThreadId=tid,Path=selectable?path:"",Title=title.ToString(),ClassName=cls.ToString(),Bounds=r.Box,WindowKind=kind,Selectable=selectable};
        }
        internal static bool IsPopup(WindowInfo w)
        {
            int style=GetWindowLong(w.Handle,-16),ex=GetWindowLong(w.Handle,-20);
            bool shape=GetWindow(w.Handle,4)!=IntPtr.Zero || (ex&0x80)!=0 || ((style&0x40000)==0 && (style&0x30000)==0);
            Rectangle screen=Screen.FromRectangle(w.Bounds).Bounds;
            return shape && w.Bounds.Width* (long)w.Bounds.Height < screen.Width*(long)screen.Height*9/10;
        }
        internal static List<WindowInfo> VisibleWindows(){return VisibleWindows(false);}
        internal static List<WindowInfo> VisibleWindows(bool includeObstructions)
        {
            List<WindowInfo> items=new List<WindowInfo>();
            EnumWindows(delegate(IntPtr h,IntPtr p){ WindowInfo w=Read(h,true,includeObstructions); if(w!=null)items.Add(w); return true; },IntPtr.Zero);
            return items;
        }
    }

    internal sealed class WindowInfo
    {
        internal IntPtr Handle; internal uint Pid,ThreadId; internal string Path,Title,ClassName; internal Rectangle Bounds;internal int WindowKind;internal bool Selectable=true;
        internal string Key { get { return Path.ToLowerInvariant()+"|"+ClassName+"|"+Title+"|"+Bounds.Width+"x"+Bounds.Height+"|"+WindowKind; } }
        internal string Name { get { return System.IO.Path.GetFileNameWithoutExtension(Path); } }
    }
    public sealed class BlockRule
    {
        public string Id{get;set;} public string Path{get;set;} public bool FileNameOnly{get;set;}
        public string Title{get;set;} public bool ContainsTitle{get;set;} public string ClassName{get;set;}
        public int Width{get;set;} public int Height{get;set;} public int WindowKind{get;set;} public bool Enabled{get;set;}
        public int Count{get;set;} public string LastBlocked{get;set;} public string Image{get;set;} public string LastResult{get;set;}
        internal BlockRule Copy(){return (BlockRule)MemberwiseClone();}
        internal bool Matches(WindowInfo w)
        {
            bool path=String.Equals(FileNameOnly?System.IO.Path.GetFileName(w.Path):w.Path,Path,StringComparison.OrdinalIgnoreCase);
            if(!path || (!String.IsNullOrEmpty(ClassName) && !String.Equals(ClassName,w.ClassName,StringComparison.Ordinal)))return false;
            if(ContainsTitle) { if(String.IsNullOrEmpty(Title) || w.Title.IndexOf(Title,StringComparison.OrdinalIgnoreCase)<0)return false; }
            else if(!String.Equals(Title??"",w.Title,StringComparison.Ordinal))return false;
            if(FileNameOnly||ContainsTitle)return Native.IsPopup(w);
            return w.WindowKind==WindowKind && Math.Abs(w.Bounds.Width-Width)<=Math.Max(24,Width/12) && Math.Abs(w.Bounds.Height-Height)<=Math.Max(24,Height/12);
        }
    }
    public sealed class WindowRecord
    {
        public string Id{get;set;} public string Key{get;set;} public string Path{get;set;} public string Title{get;set;}
        public string ClassName{get;set;} public int Width{get;set;} public int Height{get;set;} public int WindowKind{get;set;} public string Position{get;set;}
        public string FirstSeen{get;set;} public string LastSeen{get;set;} public int Count{get;set;}
        public string Image{get;set;} public string RuleId{get;set;}
        internal WindowRecord Copy(){return (WindowRecord)MemberwiseClone();}
        internal WindowInfo ToWindow(){return new WindowInfo{Path=Path,Title=Title??"",ClassName=ClassName??"",Bounds=new Rectangle(0,0,Width,Height),WindowKind=WindowKind};}
    }
    public sealed class SavedState
    {
        public int Version{get;set;} public bool Enabled{get;set;} public bool RecordHistory{get;set;}
        public List<BlockRule> Rules{get;set;} public List<WindowRecord> History{get;set;}
    }
    internal sealed class LiveWindow
    {
        internal WindowInfo Info; internal string HistoryId; internal bool Counted; internal bool ScreenshotTaken;
        internal DateTime RetryAt; internal bool IsNew;internal int RetryCount;
    }
    internal sealed class CloseAttempt
    {
        internal WindowInfo Window; internal string RuleId; internal DateTime CheckAt; internal bool Posted,Destroyed,HideRequested,WasVisible;
    }

    internal sealed class Engine : IDisposable
    {
        internal readonly string DataRoot;
        private readonly object gate=new object(),saveGate=new object();
        private SavedState state;
        private Thread thread; private Control dispatch; private ApplicationContext context;
        private System.Windows.Forms.Timer tick; private Native.EventProc callback;
        private readonly List<IntPtr> hooks=new List<IntPtr>();
        private readonly Dictionary<IntPtr,LiveWindow> live=new Dictionary<IntPtr,LiveWindow>();
        private readonly Dictionary<IntPtr,CloseAttempt> closing=new Dictionary<IntPtr,CloseAttempt>();
        private DateTime saveAt=DateTime.MaxValue,refreshAt=DateTime.MaxValue;
        private volatile bool disposed;
        internal event Action Changed;
        internal string Error="";
        internal bool Enabled {get{lock(gate)return state.Enabled;}}
        internal bool Recording {get{lock(gate)return state.RecordHistory;}}
        internal List<BlockRule> Rules {get{lock(gate)return state.Rules.Select(r=>r.Copy()).ToList();}}
        internal List<WindowRecord> History {get{lock(gate)return state.History.Select(r=>r.Copy()).OrderByDescending(r=>r.LastSeen).ToList();}}
        internal Engine(string root)
        {
            DataRoot=root; Directory.CreateDirectory(DataRoot);Directory.CreateDirectory(System.IO.Path.Combine(DataRoot,"images"));
            string file=System.IO.Path.Combine(root,"state.json");
            try{if(File.Exists(file))state=new JavaScriptSerializer().Deserialize<SavedState>(File.ReadAllText(file));}catch{Error="原记录损坏，已保留文件并重新开始记录。";try{File.Copy(file,file+".damaged-"+DateTime.Now.ToString("yyyyMMddHHmmss"));}catch{}}
            if(state==null)state=new SavedState{Version=1,Enabled=true,RecordHistory=true,Rules=new List<BlockRule>(),History=new List<WindowRecord>()};
            if(state.Rules==null)state.Rules=new List<BlockRule>();if(state.History==null)state.History=new List<WindowRecord>();
        }
        internal void Start()
        {
            ManualResetEvent ready=new ManualResetEvent(false);
            thread=new Thread(delegate(){
                dispatch=new Control();IntPtr h=dispatch.Handle;context=new ApplicationContext();callback=OnEvent;
                hooks.Add(Native.SetWinEventHook(0x8000,0x8003,IntPtr.Zero,callback,0,0,2));
                hooks.Add(Native.SetWinEventHook(0x800C,0x800C,IntPtr.Zero,callback,0,0,2));
                if(hooks.Any(x=>x==IntPtr.Zero))Error="窗口记录未启动，请退出后重新打开。";
                tick=new System.Windows.Forms.Timer{Interval=100};tick.Tick+=delegate{Tick();};tick.Start();
                ready.Set();foreach(WindowInfo w in Native.VisibleWindows())Observe(w.Handle,true,false);
                Application.Run(context);
                foreach(IntPtr hook in hooks)if(hook!=IntPtr.Zero)Native.UnhookWinEvent(hook);
                tick.Dispose();dispatch.Dispose();
            });thread.Name="窗口记录";thread.IsBackground=true;thread.SetApartmentState(ApartmentState.STA);thread.Start();
            if(ready.WaitOne(3000))ready.Dispose();else Error="窗口记录启动较慢，请稍候。";
        }
        private void OnEvent(IntPtr hook,uint ev,IntPtr hwnd,int obj,int child,uint eventThread,uint time)
        {
            if(disposed || hwnd==IntPtr.Zero || obj!=0 || child!=0)return;
            try
            {
                if(ev==0x8001){live.Remove(hwnd);CloseAttempt pending;if(closing.TryGetValue(hwnd,out pending)){pending.Destroyed=true;if(pending.CheckAt==DateTime.MaxValue)closing.Remove(hwnd);else{pending.CheckAt=DateTime.Now;FinishClosing(DateTime.Now);}}return;}
                // Our temporary hiding is not evidence that WM_CLOSE succeeded.
                if(ev==0x8003){live.Remove(hwnd);return;}
                if(ev==0x8000){CloseAttempt previous;if(closing.TryGetValue(hwnd,out previous)){previous.Destroyed=true;if(previous.CheckAt==DateTime.MaxValue)closing.Remove(hwnd);else{previous.CheckAt=DateTime.Now;FinishClosing(DateTime.Now);}}live.Remove(hwnd);}
                Observe(hwnd,false,ev==0x8002);
            }catch(Exception ex){Error="部分窗口未能记录："+ex.GetType().Name;}
        }
        private void Observe(IntPtr hwnd,bool initial,bool shown)
        {
            CloseAttempt pending;
            if(closing.TryGetValue(hwnd,out pending))
            {
                if(pending.Destroyed || !SameWindow(pending)){if(pending.CheckAt==DateTime.MaxValue)closing.Remove(hwnd);else{pending.CheckAt=DateTime.Now;FinishClosing(DateTime.Now);}}
                else if(shown&&pending.CheckAt==DateTime.MaxValue&&!pending.WasVisible)
                {
                    // CREATE may precede the first display by seconds. Allow one ordinary SHOW
                    // attempt; its WasVisible guard prevents retrying after failure/restoration.
                    closing.Remove(hwnd);
                }
                else{if(shown&&pending.CheckAt!=DateTime.MaxValue){pending.WasVisible=true;Suppress(pending);}return;}
            }
            WindowInfo w=Native.Read(hwnd,false); if(w==null)return;
            bool visible=Native.IsWindowVisible(hwnd)&&!Native.IsIconic(hwnd);
            LiveWindow slot; bool fresh=!live.TryGetValue(hwnd,out slot) || slot.Info.Pid!=w.Pid;
            if(fresh){slot=new LiveWindow{Info=w,IsNew=!initial,RetryAt=visible?DateTime.MinValue:DateTime.Now.AddMilliseconds(80)};live[hwnd]=slot;}
            bool changed=slot.Info.Key!=w.Key;slot.Info=w;
            BlockRule rule;
            lock(gate)rule=state.Enabled?state.Rules.FirstOrDefault(r=>r.Enabled&&(visible||CanMatchEarly(r))&&r.Matches(w)):null;
            // Keep the user-selected/history thumbnail; capture and PNG I/O must not delay blocking.
            if(rule!=null){TryClose(w,rule.Id,shown);return;}
            if(!visible||!Recording)return;
            lock(gate)
            {
                WindowRecord row=state.History.FirstOrDefault(r=>r.Id==slot.HistoryId);
                if(changed&&row!=null&&row.Title.Length==0&&row.Count<=1){row.Key=w.Key;row.Title=w.Title;row.ClassName=w.ClassName;row.WindowKind=w.WindowKind;}
                if(row==null || (changed&&row.Key!=w.Key))
                {
                    row=state.History.FirstOrDefault(r=>r.Key==w.Key);
                    if(row==null){row=new WindowRecord{Id=Guid.NewGuid().ToString("N"),Key=w.Key,Path=w.Path,Title=w.Title,ClassName=w.ClassName,Width=w.Bounds.Width,Height=w.Bounds.Height,WindowKind=w.WindowKind,FirstSeen=Now(),Count=0};state.History.Add(row);}
                    if(slot.HistoryId!=row.Id){slot.Counted=false;slot.ScreenshotTaken=false;}
                    slot.HistoryId=row.Id;
                }
                if(!slot.Counted){row.Count++;slot.Counted=true;}
                row.LastSeen=Now();row.Position=w.Bounds.X+", "+w.Bounds.Y;row.Width=w.Bounds.Width;row.Height=w.Bounds.Height;
                if(!slot.ScreenshotTaken||slot.RetryCount==1){string img=Capture(w);if(!String.IsNullOrEmpty(img)){DeleteIfUnusedImage(row.Image,row.Id,null);row.Image=img;slot.ScreenshotTaken=true;}}
                slot.RetryAt=slot.RetryCount<2?DateTime.Now.AddMilliseconds(120):DateTime.MinValue;
                TrimHistory();Dirty();
            }
        }
        private string Capture(WindowInfo w)
        {
            try
            {
                Rectangle rect=Rectangle.Intersect(w.Bounds,Native.Desktop);if(rect.Width<20||rect.Height<20)return "";
                using(Bitmap full=new Bitmap(rect.Width,rect.Height,PixelFormat.Format24bppRgb))
                {
                    using(Graphics g=Graphics.FromImage(full))g.CopyFromScreen(rect.Location,Point.Empty,rect.Size,CopyPixelOperation.SourceCopy);
                    double ratio=Math.Min(1.0,Math.Min(520.0/rect.Width,320.0/rect.Height));
                    using(Bitmap thumb=new Bitmap(Math.Max(1,(int)(rect.Width*ratio)),Math.Max(1,(int)(rect.Height*ratio))))
                    {
                        using(Graphics g=Graphics.FromImage(thumb)){g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(full,0,0,thumb.Width,thumb.Height);}
                        string name=System.IO.Path.Combine("images",Guid.NewGuid().ToString("N")+".png");thumb.Save(System.IO.Path.Combine(DataRoot,name),ImageFormat.Png);return name;
                    }
                }
            }catch{return "";}
        }
        private void TrimHistory()
        {
            while(state.History.Count>300){WindowRecord old=state.History.OrderBy(r=>r.LastSeen).First();state.History.Remove(old);DeleteIfUnusedImage(old.Image,null,null);}
        }
        private void DeleteIfUnusedImage(string image,string ignoreHistory,string ignoreRule)
        {
            if(String.IsNullOrEmpty(image))return;
            if(state.History.Any(h=>h.Id!=ignoreHistory && h.Image==image) || state.Rules.Any(r=>r.Id!=ignoreRule && r.Image==image))return;
            try{string full=ImagePath(image);if(full!=null)File.Delete(full);}catch{}
        }
        internal string ImagePath(string relative)
        {
            if(String.IsNullOrEmpty(relative))return null;
            string p=System.IO.Path.GetFullPath(System.IO.Path.Combine(DataRoot,relative));
            return p.StartsWith(DataRoot.TrimEnd('\\')+"\\images\\",StringComparison.OrdinalIgnoreCase)?p:null;
        }
        private static bool CanMatchEarly(BlockRule r)
        {
            return !r.FileNameOnly&&!r.ContainsTitle&&!String.IsNullOrEmpty(r.Path)&&!String.IsNullOrEmpty(r.ClassName)&&!String.IsNullOrEmpty(r.Title)&&r.Width>=40&&r.Height>=25;
        }
        private static bool SameWindow(CloseAttempt a)
        {
            if(a.Destroyed||!Native.IsWindow(a.Window.Handle))return false;
            uint pid;uint tid=Native.GetWindowThreadProcessId(a.Window.Handle,out pid);
            StringBuilder cls=new StringBuilder(256);Native.GetClassName(a.Window.Handle,cls,cls.Capacity);
            return pid==a.Window.Pid&&tid==a.Window.ThreadId&&cls.ToString()==a.Window.ClassName;
        }
        private void Restore(CloseAttempt a)
        {
            // Queue restoration even if still visible: the preceding async hide may not have run yet.
            if(a.HideRequested&&a.WasVisible&&SameWindow(a))Native.ShowWindowAsync(a.Window.Handle,8);
            a.HideRequested=false;
        }
        private bool Suppress(CloseAttempt a)
        {
            if(!SameWindow(a))return false;
            WindowInfo current=Native.Read(a.Window.Handle,false);
            bool allowed;lock(gate)allowed=current!=null&&state.Enabled&&state.Rules.Any(r=>r.Id==a.RuleId&&r.Enabled&&r.Matches(current));
            if(!allowed){a.CheckAt=DateTime.MaxValue;Restore(a);return false;}
            a.WasVisible|=Native.IsWindowVisible(a.Window.Handle);
            // An initially invisible CREATE needs only early WM_CLOSE. Hide once SHOW is known,
            // so a delayed hide cannot strand a never-shown window when cancellation drops tracking.
            if(a.WasVisible)a.HideRequested|=Native.ShowWindowAsync(a.Window.Handle,0);
            return true;
        }
        private void CancelAttempts(string ruleId)
        {
            foreach(IntPtr h in closing.Where(k=>ruleId==null||k.Value.RuleId==ruleId).Select(k=>k.Key).ToList()){Restore(closing[h]);closing.Remove(h);}
        }
        private void TryClose(WindowInfo w,string id,bool shown=false)
        {
            WindowInfo current=Native.Read(w.Handle,false);
            BlockRule r;lock(gate)r=state.Enabled?state.Rules.FirstOrDefault(x=>x.Id==id&&x.Enabled):null;
            if(current==null || current.Pid!=w.Pid || current.ThreadId!=w.ThreadId || r==null || !r.Matches(current) || (!Native.IsWindowVisible(w.Handle)&&!CanMatchEarly(r)))return;
            CloseAttempt a=new CloseAttempt{Window=current,RuleId=id,WasVisible=shown||Native.IsWindowVisible(w.Handle),CheckAt=DateTime.Now.AddMilliseconds(700)};
            closing[w.Handle]=a;if(!Suppress(a)){closing.Remove(w.Handle);return;}
            a.Posted=Native.PostMessage(w.Handle,0x0010,IntPtr.Zero,IntPtr.Zero);
            if(!a.Posted)a.CheckAt=DateTime.Now;
        }
        private void Tick()
        {
            DateTime now=DateTime.Now;
            foreach(IntPtr key in live.Where(k=>k.Value.RetryAt!=DateTime.MinValue&&k.Value.RetryAt<=now).Select(k=>k.Key).ToList())
            {
                LiveWindow slot=live[key];slot.RetryAt=DateTime.MinValue;slot.RetryCount++;
                Observe(key,false,false);
            }
            FinishClosing(now);
            if(now>=saveAt){saveAt=DateTime.MaxValue;Save();}
            if(now>=refreshAt){refreshAt=DateTime.MaxValue;Action changed=Changed;if(changed!=null)changed();}
        }
        private void FinishClosing(DateTime now)
        {
            foreach(IntPtr h in closing.Where(k=>k.Value.CheckAt!=DateTime.MaxValue&&k.Value.CheckAt<=now).Select(k=>k.Key).ToList())
            {
                CloseAttempt a=closing[h];bool gone=!SameWindow(a);
                // Set the lifetime failure guard before restoration can produce a reentrant SHOW.
                if(!gone)a.CheckAt=DateTime.MaxValue;
                if(!gone)Restore(a);
                lock(gate)
                {
                    BlockRule r=state.Rules.FirstOrDefault(x=>x.Id==a.RuleId);
                    if(r!=null)
                    {
                        if(a.Posted&&gone){r.Count++;r.LastBlocked=Now();r.LastResult="已拦截";}
                        else r.LastResult=a.Posted?"窗口未关闭":"未能关闭，可能需要同等权限";
                        Dirty();
                    }
                }
                // Keep failed attempts attached to this window lifetime; no repeated close spam.
                if(gone)closing.Remove(h);else a.CheckAt=DateTime.MaxValue;
            }
        }
        private void Dirty(){if(saveAt==DateTime.MaxValue)saveAt=DateTime.Now.AddMilliseconds(500);if(refreshAt==DateTime.MaxValue)refreshAt=DateTime.Now.AddMilliseconds(120);}
        private static string Now(){return DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fff");}
        private void OnWorker(Action action){if(dispatch!=null&&!disposed)try{dispatch.BeginInvoke(action);}catch{}}
        internal void SetEnabled(bool enabled){OnWorker(delegate{lock(gate){state.Enabled=enabled;Dirty();}if(!enabled)CancelAttempts(null);else foreach(WindowInfo w in Native.VisibleWindows())Observe(w.Handle,true,false);});}
        internal void SetRecording(bool enabled){OnWorker(delegate{lock(gate){state.RecordHistory=enabled;Dirty();}if(enabled)foreach(WindowInfo w in Native.VisibleWindows())Observe(w.Handle,true,false);});}
        internal void ToggleRule(string id,bool enabled){OnWorker(delegate{lock(gate){BlockRule r=state.Rules.FirstOrDefault(x=>x.Id==id);if(r!=null)r.Enabled=enabled;Dirty();}if(!enabled)CancelAttempts(id);else foreach(WindowInfo w in Native.VisibleWindows())Observe(w.Handle,true,false);});}
        internal void RemoveRule(string id){OnWorker(delegate{lock(gate){BlockRule r=state.Rules.FirstOrDefault(x=>x.Id==id);if(r!=null){state.Rules.Remove(r);DeleteIfUnusedImage(r.Image,null,null);}foreach(WindowRecord h in state.History.Where(x=>x.RuleId==id))h.RuleId=null;Dirty();}CancelAttempts(id);});}
        internal void ClearHistory(){OnWorker(delegate{lock(gate){List<string> imgs=state.History.Select(h=>h.Image).ToList();state.History.Clear();foreach(string img in imgs)DeleteIfUnusedImage(img,null,null);foreach(LiveWindow w in live.Values){w.HistoryId=null;w.Counted=false;w.ScreenshotTaken=false;}Dirty();}});}
        internal void AddFromHistory(string id){OnWorker(delegate{lock(gate){WindowRecord h=state.History.FirstOrDefault(x=>x.Id==id);if(h==null)return;h.RuleId=AddRule(h.ToWindow(),h.Image);Dirty();}foreach(WindowInfo w in Native.VisibleWindows())Observe(w.Handle,true,false);});}
        internal void AddSelected(WindowInfo w,Bitmap image)
        {
            string saved="";
            if(image!=null){try{saved=System.IO.Path.Combine("images",Guid.NewGuid().ToString("N")+".png");image.Save(System.IO.Path.Combine(DataRoot,saved),ImageFormat.Png);}catch{saved="";}finally{image.Dispose();}}
            string thumbnail=saved;
            OnWorker(delegate{string id;lock(gate){id=AddRule(w,thumbnail);foreach(WindowRecord row in state.History.Where(x=>x.Key==w.Key))row.RuleId=id;Dirty();}WindowInfo current=Native.Read(w.Handle,true);if(current!=null&&current.Pid==w.Pid&&current.ThreadId==w.ThreadId&&!closing.ContainsKey(w.Handle))TryClose(current,id);});
        }
        private string AddRule(WindowInfo w,string image)
        {
            BlockRule existing=state.Rules.FirstOrDefault(r=>!r.FileNameOnly&&!r.ContainsTitle&&r.Path.Equals(w.Path,StringComparison.OrdinalIgnoreCase)&&r.ClassName==w.ClassName&&r.Title==w.Title&&r.WindowKind==w.WindowKind&&r.Width==w.Bounds.Width&&r.Height==w.Bounds.Height);
            if(existing!=null){existing.Enabled=true;DeleteIfUnusedImage(image,null,null);return existing.Id;}
            BlockRule rule=new BlockRule{Id=Guid.NewGuid().ToString("N"),Path=w.Path,Title=w.Title,ClassName=w.ClassName,Width=w.Bounds.Width,Height=w.Bounds.Height,WindowKind=w.WindowKind,Enabled=true,Count=0,Image=image,LastResult="等待再次出现"};state.Rules.Add(rule);return rule.Id;
        }
        internal void Save()
        {
            lock(saveGate)
            {
                string json;lock(gate)json=new JavaScriptSerializer{MaxJsonLength=8*1024*1024}.Serialize(state);
                string file=System.IO.Path.Combine(DataRoot,"state.json"),temp=file+".tmp";
                try{File.WriteAllText(temp,json,new UTF8Encoding(false));if(File.Exists(file))File.Replace(temp,file,file+".bak",true);else File.Move(temp,file);}
                catch{Error="记录暂时无法保存，请检查数据文件夹是否可写。";}
            }
        }
        public void Dispose()
        {
            if(disposed)return;
            if(dispatch!=null)try{dispatch.Invoke((Action)delegate{disposed=true;FinishClosing(DateTime.MaxValue);CancelAttempts(null);Save();context.ExitThread();});}catch{disposed=true;}
            if(thread!=null)thread.Join(2000);
        }
    }

    internal static class Ui
    {
        internal static readonly Color Accent=Color.FromArgb(230,171,34),Ink=Color.FromArgb(45,48,52),Line=Color.FromArgb(231,233,236);
        internal static Button Button(string text,int width)
        {
            Button b=new Button{Text=text,Width=width,Height=34,FlatStyle=FlatStyle.Flat,BackColor=Color.White,ForeColor=Ink,Margin=new Padding(6,0,0,0)};
            b.FlatAppearance.BorderColor=Color.FromArgb(207,210,215);return b;
        }
        internal static DataGridView Grid()
        {
            DataGridView g=new DataGridView{Dock=DockStyle.Fill,BackgroundColor=Color.White,BorderStyle=BorderStyle.None,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AllowUserToResizeRows=false,RowHeadersVisible=false,AutoGenerateColumns=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal,ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.None,EnableHeadersVisualStyles=false};
            g.DefaultCellStyle.Font=new Font("Microsoft YaHei UI",9F);g.DefaultCellStyle.ForeColor=Ink;g.DefaultCellStyle.SelectionBackColor=Color.FromArgb(255,245,216);g.DefaultCellStyle.SelectionForeColor=Ink;g.DefaultCellStyle.Padding=new Padding(6,0,4,0);g.GridColor=Line;g.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(246,247,249);g.ColumnHeadersDefaultCellStyle.ForeColor=Color.FromArgb(100,105,112);g.ColumnHeadersDefaultCellStyle.Font=new Font("Microsoft YaHei UI",9F);g.ColumnHeadersDefaultCellStyle.WrapMode=DataGridViewTriState.False;g.ColumnHeadersHeight=36;g.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing;g.RowTemplate.Height=40;return g;
        }
        internal static void TextColumn(DataGridView g,string name,string header,int width,bool fill)
        {
            g.Columns.Add(new DataGridViewTextBoxColumn{Name=name,HeaderText=header,Width=width,ReadOnly=true,AutoSizeMode=fill?DataGridViewAutoSizeColumnMode.Fill:DataGridViewAutoSizeColumnMode.None,MinimumWidth=60,SortMode=DataGridViewColumnSortMode.NotSortable});
        }
        internal static Image LoadPreview(Engine engine,string image)
        {
            try{string path=engine.ImagePath(image);if(path!=null&&File.Exists(path))using(Image loaded=Image.FromFile(path))return new Bitmap(loaded);}catch{}return null;
        }
        internal static string Time(string value){DateTime d;return DateTime.TryParse(value,out d)?d.ToString("MM-dd HH:mm:ss"):"—";}
        internal static string Name(string path){try{return System.IO.Path.GetFileNameWithoutExtension(path);}catch{return path;}}
        internal static void ReplaceImage(PictureBox box,Image image){Image old=box.Image;box.Image=image;if(old!=null)old.Dispose();}
        internal static void UpdateRows(DataGridView grid,List<GridRow> rows,bool newestFirst)
        {
            string selected=grid.SelectedRows.Count>0?grid.SelectedRows[0].Tag as string:null;
            HashSet<string> ids=new HashSet<string>(rows.Select(r=>r.Id));
            for(int i=grid.Rows.Count-1;i>=0;i--)if(!ids.Contains(grid.Rows[i].Tag as string))grid.Rows.RemoveAt(i);
            Dictionary<string,DataGridViewRow> existing=grid.Rows.Cast<DataGridViewRow>().Where(r=>r.Tag is string).ToDictionary(r=>(string)r.Tag,r=>r);
            IEnumerable<GridRow> ordered=newestFirst?rows.AsEnumerable().Reverse():rows;
            foreach(GridRow value in ordered)
            {
                DataGridViewRow row;
                if(!existing.TryGetValue(value.Id,out row))
                {
                    int index;if(newestFirst){grid.Rows.Insert(0,value.Values);index=0;}else index=grid.Rows.Add(value.Values);
                    row=grid.Rows[index];row.Tag=value.Id;existing[value.Id]=row;
                }
                else for(int c=0;c<value.Values.Length;c++)if(!Object.Equals(row.Cells[c].Value,value.Values[c]))row.Cells[c].Value=value.Values[c];
            }
            DataGridViewRow chosen;if(selected!=null&&existing.TryGetValue(selected,out chosen)&&(!Object.ReferenceEquals(grid.CurrentRow,chosen)))grid.CurrentCell=chosen.Cells[Math.Min(1,grid.Columns.Count-1)];
        }
    }
    internal sealed class GridRow{internal string Id;internal object[] Values;}

    internal sealed class MainForm : Form
    {
        private readonly Engine engine; private readonly DataGridView grid; private readonly Label summary,detail;
        private readonly CheckBox enable;private readonly PictureBox preview;private readonly NotifyIcon tray;private bool refreshing,exit;
        private readonly HashSet<string> pendingDeletions=new HashSet<string>();
        private List<BlockRule> displayedRules=new List<BlockRule>();
        private HistoryForm history;private readonly System.Windows.Forms.Timer refreshTimer;private readonly EventWaitHandle shutdown,openRequest;
        internal MainForm(Engine engine,bool hidden,EventWaitHandle shutdown,EventWaitHandle openRequest)
        {
            this.engine=engine;this.shutdown=shutdown;this.openRequest=openRequest;Text="弹窗拦截";Name="PopupGuardMain";Font=new Font("Microsoft YaHei UI",9F);ForeColor=Ui.Ink;BackColor=Color.White;Size=new Size(860,590);MinimumSize=new Size(760,480);StartPosition=FormStartPosition.CenterScreen;Icon=SystemIcons.Shield;
            TableLayoutPanel layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24,20,24,16),ColumnCount=1,RowCount=5};layout.RowStyles.Add(new RowStyle(SizeType.Absolute,58));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,42));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,108));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,24));Controls.Add(layout);
            Panel header=new Panel{Dock=DockStyle.Fill};Label title=new Label{Text="弹窗拦截",Font=new Font(Font.FontFamily,18F,FontStyle.Bold),AutoSize=true,Location=new Point(0,0)};header.Controls.Add(title);
            FlowLayoutPanel actions=new FlowLayoutPanel{Dock=DockStyle.Right,Width=248,FlowDirection=FlowDirection.LeftToRight,WrapContents=false};Button pick=Ui.Button("截图拦截",114),past=Ui.Button("历史弹窗",114);pick.Name="PickPopup";past.Name="OpenHistory";pick.BackColor=Ui.Accent;pick.FlatAppearance.BorderColor=Ui.Accent;pick.Click+=delegate{Pick();};past.Click+=delegate{OpenHistory();};actions.Controls.Add(pick);actions.Controls.Add(past);header.Controls.Add(actions);layout.Controls.Add(header,0,0);
            Panel bar=new Panel{Dock=DockStyle.Fill};enable=new CheckBox{Text="开启弹窗拦截",AutoSize=true,Checked=engine.Enabled,Location=new Point(0,6),Name="EnableBlocking"};enable.CheckedChanged+=delegate{if(!refreshing)engine.SetEnabled(enable.Checked);};bar.Controls.Add(enable);summary=new Label{AutoSize=true,Anchor=AnchorStyles.Top|AnchorStyles.Right,Location=new Point(350,8)};bar.Controls.Add(summary);bar.Resize+=delegate{summary.Left=Math.Max(200,bar.Width-summary.Width);};layout.Controls.Add(bar,0,1);
            grid=Ui.Grid();grid.Name="BlockedWindows";grid.Columns.Add(new DataGridViewCheckBoxColumn{Name="On",HeaderText="拦截",Width=88,MinimumWidth=88,SortMode=DataGridViewColumnSortMode.NotSortable});grid.Columns[0].HeaderCell.Style=new DataGridViewCellStyle{WrapMode=DataGridViewTriState.False,Alignment=DataGridViewContentAlignment.MiddleCenter,Padding=Padding.Empty};Ui.TextColumn(grid,"App","软件",125,false);Ui.TextColumn(grid,"Title","弹窗名称",200,true);Ui.TextColumn(grid,"Count","已拦截",80,false);Ui.TextColumn(grid,"Last","最近拦截",125,false);
            grid.Columns.Add(new DataGridViewButtonColumn{Name="DeleteRule",HeaderText="操作",Text="删除",UseColumnTextForButtonValue=true,Width=68,MinimumWidth=64,ReadOnly=true,FlatStyle=FlatStyle.Flat,SortMode=DataGridViewColumnSortMode.NotSortable});
            grid.CellContentClick+=delegate(object s,DataGridViewCellEventArgs e){if(e.RowIndex<0||e.ColumnIndex<0||grid.Columns[e.ColumnIndex].Name!="DeleteRule")return;string id=grid.Rows[e.RowIndex].Tag as string;if(id!=null)DeleteRuleNow(id);};
            grid.CurrentCellDirtyStateChanged+=delegate{if(grid.IsCurrentCellDirty)grid.CommitEdit(DataGridViewDataErrorContexts.Commit);};grid.CellValueChanged+=delegate(object s,DataGridViewCellEventArgs e){if(!refreshing&&e.RowIndex>=0&&e.ColumnIndex==0){string id=(string)grid.Rows[e.RowIndex].Tag;engine.ToggleRule(id,Convert.ToBoolean(grid.Rows[e.RowIndex].Cells[0].Value));}};grid.SelectionChanged+=delegate{if(!refreshing)ShowSelected();};grid.CellDoubleClick+=delegate{ShowSelected();};
            ContextMenuStrip menu=new ContextMenuStrip();menu.Items.Add("取消这条拦截",null,delegate{string id=SelectedId();if(id!=null)DeleteRuleNow(id);});grid.ContextMenuStrip=menu;grid.MouseDown+=delegate(object s,MouseEventArgs e){if(e.Button==MouseButtons.Right){DataGridView.HitTestInfo hit=grid.HitTest(e.X,e.Y);if(hit.RowIndex>=0)grid.CurrentCell=grid.Rows[hit.RowIndex].Cells[1];}};layout.Controls.Add(grid,0,2);
            Panel foot=new Panel{Dock=DockStyle.Fill,Padding=new Padding(0,14,0,0)};preview=new PictureBox{Location=new Point(0,14),Size=new Size(140,82),SizeMode=PictureBoxSizeMode.Zoom,BackColor=Color.FromArgb(246,247,249),Name="BlockedPreview"};detail=new Label{Location=new Point(154,18),Size=new Size(620,80),AutoEllipsis=true,Text="暂无拦截记录。点击“截图拦截”，选择要拦截的弹窗。"};foot.Controls.Add(preview);foot.Controls.Add(detail);layout.Controls.Add(foot,0,3);
            Label note=new Label{Text="取消勾选可暂停拦截，点“删除”移除规则。关闭窗口后继续在托盘运行。",Dock=DockStyle.Fill,ForeColor=Color.FromArgb(128,133,141)};layout.Controls.Add(note,0,4);
            tray=new NotifyIcon{Icon=SystemIcons.Shield,Text="弹窗拦截",Visible=true};ContextMenuStrip trayMenu=new ContextMenuStrip();trayMenu.Items.Add("打开弹窗拦截",null,delegate{Restore();});trayMenu.Items.Add("截图拦截",null,delegate{Pick();});trayMenu.Items.Add("历史弹窗",null,delegate{Restore();OpenHistory();});trayMenu.Items.Add(new ToolStripSeparator());trayMenu.Items.Add("退出",null,delegate{exit=true;Close();});tray.ContextMenuStrip=trayMenu;
            tray.MouseClick+=delegate(object s,MouseEventArgs e){if(e.Button==MouseButtons.Left)Restore();};
            FormClosing+=delegate(object s,FormClosingEventArgs e){if(!exit&&e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();}else{refreshTimer.Stop();tray.Visible=false;tray.Dispose();engine.Dispose();}};
            refreshTimer=new System.Windows.Forms.Timer{Interval=1000};refreshTimer.Tick+=delegate{if(shutdown.WaitOne(0)){exit=true;foreach(PickerForm p in Application.OpenForms.OfType<PickerForm>().ToArray())p.Close();Close();}else{if(openRequest.WaitOne(0)&&!Application.OpenForms.OfType<PickerForm>().Any())Restore();RefreshRows();}};refreshTimer.Start();RefreshRows();
            Shown+=delegate{if(hidden)Hide();};
        }
        private void Restore(){Show();WindowState=FormWindowState.Normal;Activate();}
        private string SelectedId(){return grid.SelectedRows.Count>0?grid.SelectedRows[0].Tag as string:null;}
        private void DeleteRuleNow(string id)
        {
            if(!pendingDeletions.Add(id))return;
            displayedRules.RemoveAll(r=>r.Id==id);refreshing=true;
            try
            {
                DataGridViewRow row=grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r=>(r.Tag as string)==id);
                if(row!=null)grid.Rows.Remove(row);
                UpdateSummary();ShowSelected();
            }finally{refreshing=false;}
            engine.RemoveRule(id);
        }
        private void UpdateSummary()
        {
            summary.Text="累计拦截 "+displayedRules.Sum(r=>r.Count)+" 次  ·  "+displayedRules.Count+" 条规则";summary.Left=Math.Max(200,summary.Parent.Width-summary.Width);
        }
        private void RefreshRows()
        {
            if(IsDisposed)return;List<BlockRule> allRules=engine.Rules;HashSet<string> existingIds=new HashSet<string>(allRules.Select(r=>r.Id));pendingDeletions.RemoveWhere(id=>!existingIds.Contains(id));List<BlockRule> rules=allRules.Where(r=>!pendingDeletions.Contains(r.Id)).ToList();displayedRules=rules;refreshing=true;
            try
            {
                enable.Checked=engine.Enabled;Ui.UpdateRows(grid,rules.Select(r=>new GridRow{Id=r.Id,Values=new object[]{r.Enabled,Ui.Name(r.Path),String.IsNullOrEmpty(r.Title)?"无标题弹窗":r.Title,r.Count+" 次",Ui.Time(r.LastBlocked)}}).ToList(),false);
                UpdateSummary();tray.Text=engine.Enabled?"弹窗拦截 · 运行中":"弹窗拦截 · 已暂停";ShowSelected();if(history!=null&&!history.IsDisposed)history.RefreshRows();
            }finally{refreshing=false;}
        }
        private void ShowSelected()
        {
            string id=SelectedId();BlockRule r=displayedRules.FirstOrDefault(x=>x.Id==id);
            if(r==null){Ui.ReplaceImage(preview,null);detail.Text=String.IsNullOrEmpty(engine.Error)?"暂无选择。点击“截图拦截”或从“历史弹窗”中添加拦截。":engine.Error;return;}
            Ui.ReplaceImage(preview,Ui.LoadPreview(engine,r.Image));detail.Text=(r.Title.Length==0?"无标题弹窗":r.Title)+"\n"+r.Path+"\n"+(r.Enabled?(r.LastResult??"等待再次出现"):"已取消拦截")+"  ·  "+r.Count+" 次";
        }
        private void OpenHistory(){if(history==null||history.IsDisposed){history=new HistoryForm(engine);history.Show(this);}else{history.Show();history.Activate();}}
        private void Pick()
        {
            if(history!=null&&!history.IsDisposed)history.Hide();Hide();
            System.Windows.Forms.Timer delay=new System.Windows.Forms.Timer{Interval=180};delay.Tick+=delegate{delay.Stop();delay.Dispose();
                try{using(PickerForm picker=new PickerForm()){if(picker.ShowDialog()==DialogResult.OK&&picker.Selected!=null)engine.AddSelected(picker.Selected,picker.DetachThumbnail());}}
                catch(Exception ex){MessageBox.Show("未能开始选取窗口："+ex.Message,"弹窗拦截");}finally{if(!exit){Restore();RefreshRows();}}
            };delay.Start();
        }
    }

    internal sealed class HistoryForm : Form
    {
        private readonly Engine engine;private readonly DataGridView grid;private readonly CheckBox recording;
        private readonly TextBox search;private readonly PictureBox preview;private readonly Label detail,total;
        private readonly Button block;private bool refreshing;
        internal HistoryForm(Engine engine)
        {
            this.engine=engine;Text="历史弹窗";Name="PopupHistory";Font=new Font("Microsoft YaHei UI",9F);BackColor=Color.White;ForeColor=Ui.Ink;Size=new Size(900,650);MinimumSize=new Size(800,540);StartPosition=FormStartPosition.CenterParent;Icon=SystemIcons.Shield;
            TableLayoutPanel layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(22,18,22,16),ColumnCount=1,RowCount=5};layout.RowStyles.Add(new RowStyle(SizeType.Absolute,48));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,44));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,146));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,25));Controls.Add(layout);
            Panel header=new Panel{Dock=DockStyle.Fill};recording=new CheckBox{Text="记录历史弹窗",AutoSize=true,Checked=engine.Recording,Location=new Point(0,8),Name="RecordHistory"};recording.CheckedChanged+=delegate{if(!refreshing)engine.SetRecording(recording.Checked);};total=new Label{Dock=DockStyle.Right,Width=240,TextAlign=ContentAlignment.MiddleRight};header.Controls.Add(recording);header.Controls.Add(total);layout.Controls.Add(header,0,0);
            Panel tools=new Panel{Dock=DockStyle.Fill};search=new TextBox{Width=290,Location=new Point(0,4),Name="HistorySearch"};search.TextChanged+=delegate{RefreshRows();};search.AccessibleName="搜索软件或弹窗名称";Label searchLabel=new Label{Text="搜索软件或弹窗名称",AutoSize=true,ForeColor=Color.Gray,Location=new Point(306,8)};Button clear=Ui.Button("清空记录",102);clear.Anchor=AnchorStyles.Top|AnchorStyles.Right;clear.Left=tools.Width-102;tools.Resize+=delegate{clear.Left=tools.Width-102;};clear.Click+=delegate{engine.ClearHistory();};tools.Controls.Add(search);tools.Controls.Add(searchLabel);tools.Controls.Add(clear);layout.Controls.Add(tools,0,1);
            grid=Ui.Grid();grid.Name="HistoryWindows";grid.ReadOnly=true;Ui.TextColumn(grid,"App","软件",110,false);Ui.TextColumn(grid,"Title","弹窗名称",210,true);Ui.TextColumn(grid,"Count","出现次数",78,false);Ui.TextColumn(grid,"Time","最近出现",123,false);Ui.TextColumn(grid,"State","状态",100,false);grid.SelectionChanged+=delegate{if(!refreshing)SelectedChanged();};grid.CellDoubleClick+=delegate{SelectedChanged();};layout.Controls.Add(grid,0,2);
            Panel footer=new Panel{Dock=DockStyle.Fill};preview=new PictureBox{Location=new Point(0,12),Size=new Size(205,120),BackColor=Color.FromArgb(246,247,249),SizeMode=PictureBoxSizeMode.Zoom,Name="HistoryPreview"};detail=new Label{Location=new Point(220,16),Size=new Size(600, seventy()),AutoEllipsis=true};block=Ui.Button("拦截此弹窗",132);block.Name="BlockHistoricalPopup";block.BackColor=Ui.Accent;block.FlatAppearance.BorderColor=Ui.Accent;block.Location=new Point(220,98);block.Click+=delegate{string id=SelectedId();if(id!=null){engine.AddFromHistory(id);block.Enabled=false;}};footer.Controls.Add(preview);footer.Controls.Add(detail);footer.Controls.Add(block);layout.Controls.Add(footer,0,3);
            Label note=new Label{Dock=DockStyle.Fill,Text="记录开启后看到的窗口，保留最近 300 种；极短暂的窗口可能来不及截图。",ForeColor=Color.FromArgb(128,133,141)};layout.Controls.Add(note,0,4);RefreshRows();FormClosed+=delegate{Ui.ReplaceImage(preview,null);};
        }
        private static int seventy(){return 76;}
        private string SelectedId(){return grid.SelectedRows.Count>0?grid.SelectedRows[0].Tag as string:null;}
        internal void RefreshRows()
        {
            if(IsDisposed)return;string id=SelectedId(),q=search.Text.Trim();List<WindowRecord> records=engine.History;
            refreshing=true;try{recording.Checked=engine.Recording;Ui.UpdateRows(grid,records.Where(h=>q.Length==0||((h.Title??"")+h.Path).IndexOf(q,StringComparison.OrdinalIgnoreCase)>=0).Select(h=>new GridRow{Id=h.Id,Values=new object[]{Ui.Name(h.Path),String.IsNullOrEmpty(h.Title)?"无标题弹窗":h.Title,h.Count+" 次",Ui.Time(h.LastSeen),String.IsNullOrEmpty(h.RuleId)?"未拦截":"已设拦截"}}).ToList(),true);total.Text="已记录 "+records.Count+" 种弹窗";SelectedChanged();}finally{refreshing=false;}
        }
        private void SelectedChanged()
        {
            WindowRecord h=engine.History.FirstOrDefault(x=>x.Id==SelectedId());
            if(h==null){Ui.ReplaceImage(preview,null);detail.Text="选择一条记录，查看当时的弹窗并添加拦截。";block.Enabled=false;return;}
            Ui.ReplaceImage(preview,Ui.LoadPreview(engine,h.Image));detail.Text=(String.IsNullOrEmpty(h.Title)?"无标题弹窗":h.Title)+"\n"+h.Path+"\n"+Ui.Time(h.LastSeen)+"  ·  出现 "+h.Count+" 次  ·  "+(preview.Image==null?"未能截图":"当时屏幕预览");block.Enabled=String.IsNullOrEmpty(h.RuleId)||!engine.Rules.Any(r=>r.Id==h.RuleId&&r.Enabled);
        }
    }

    internal sealed class PickerForm : Form
    {
        private readonly Rectangle desktop;private readonly Bitmap capture;private readonly List<WindowInfo> windows;
        private WindowInfo hover,selected;private readonly Panel card;private readonly Label info;private readonly Button accept;
        internal WindowInfo Selected{get{return selected;}}
        internal PickerForm()
        {
            desktop=Native.Desktop;windows=Native.VisibleWindows(true);capture=new Bitmap(desktop.Width,desktop.Height,PixelFormat.Format24bppRgb);
            using(Graphics g=Graphics.FromImage(capture))g.CopyFromScreen(desktop.Location,Point.Empty,desktop.Size);
            Text="截图拦截 - 选择弹窗";Name="PopupPicker";AutoScaleMode=AutoScaleMode.None;FormBorderStyle=FormBorderStyle.None;Bounds=desktop;TopMost=true;ShowInTaskbar=true;KeyPreview=true;Cursor=Cursors.Cross;DoubleBuffered=true;
            card=new Panel{Size=new Size(390,132),BackColor=Color.White,Visible=false};info=new Label{Location=new Point(15,10),Size=new Size(360,57),AutoEllipsis=true,Font=new Font("Microsoft YaHei UI",9F)};accept=Ui.Button("拦截",102);accept.Name="ConfirmPickedPopup";accept.Location=new Point(267,82);accept.BackColor=Ui.Accent;accept.FlatAppearance.BorderColor=Ui.Accent;accept.Click+=delegate{if(selected!=null){DialogResult=DialogResult.OK;Close();}};Button cancel=Ui.Button("取消",88);cancel.Location=new Point(160,82);cancel.Click+=delegate{DialogResult=DialogResult.Cancel;Close();};card.Controls.Add(info);card.Controls.Add(accept);card.Controls.Add(cancel);Controls.Add(card);
            KeyDown+=delegate(object s,KeyEventArgs e){if(e.KeyCode==Keys.Escape){DialogResult=DialogResult.Cancel;Close();}};
            MouseMove+=delegate(object s,MouseEventArgs e){if(selected==null){WindowInfo next=Hit(e.Location);if((next==null?IntPtr.Zero:next.Handle)!=(hover==null?IntPtr.Zero:hover.Handle)){hover=next;Invalidate();}}};
            MouseDown+=delegate(object s,MouseEventArgs e){if(e.Button==MouseButtons.Right){DialogResult=DialogResult.Cancel;Close();return;}if(e.Button!=MouseButtons.Left)return;WindowInfo hit=Hit(e.Location);if(hit==null)return;selected=hit;hover=hit;info.Text=hit.Name+"  ·  "+(hit.Title.Length==0?"无标题弹窗":hit.Title)+"\n下次出现这类窗口时自动拦截。";accept.Enabled=true;if(!Native.IsPopup(hit))info.Text=hit.Name+"  ·  "+hit.Title+"\n请确认选中的是广告弹窗，而不是软件主界面。";Rectangle b=Local(hit.Bounds);int x=Math.Max(8,Math.Min(Width-card.Width-8,b.Left)),y=b.Bottom+10;if(y+card.Height>Height)y=Math.Max(8,b.Top-card.Height-10);card.Location=new Point(x,y);card.Visible=true;Invalidate();};
        }
        private WindowInfo Hit(Point p){Point screen=new Point(p.X+desktop.X,p.Y+desktop.Y);WindowInfo first=windows.FirstOrDefault(w=>w.Bounds.Contains(screen));return first!=null&&first.Selectable?first:null;}
        private Rectangle Local(Rectangle r){r.Offset(-desktop.X,-desktop.Y);return r;}
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.DrawImageUnscaled(capture,0,0);using(Brush dim=new SolidBrush(Color.FromArgb(95,0,0,0)))e.Graphics.FillRectangle(dim,ClientRectangle);
            WindowInfo target=selected??hover;if(target!=null){Rectangle r=Local(target.Bounds),visible=Rectangle.Intersect(r,ClientRectangle);if(visible.Width>0&&visible.Height>0)e.Graphics.DrawImage(capture,visible,visible,GraphicsUnit.Pixel);using(Pen pen=new Pen(Ui.Accent,3))e.Graphics.DrawRectangle(pen,r);}
            string text="移动鼠标选择弹窗，单击后点“拦截”  ·  Esc / 右键取消";using(Font f=new Font("Microsoft YaHei UI",12F)){SizeF size=e.Graphics.MeasureString(text,f);RectangleF box=new RectangleF((Width-size.Width)/2-18,24,size.Width+36,45);using(Brush b=new SolidBrush(Color.FromArgb(235,255,255,255)))e.Graphics.FillRectangle(b,box);using(Brush b=new SolidBrush(Ui.Ink))e.Graphics.DrawString(text,f,b,box.X+18,box.Y+10);}
        }
        internal Bitmap DetachThumbnail()
        {
            if(selected==null)return null;Rectangle r=Rectangle.Intersect(Local(selected.Bounds),new Rectangle(Point.Empty,capture.Size));if(r.Width<=0||r.Height<=0)return null;
            double ratio=Math.Min(1.0,Math.Min(520.0/r.Width,320.0/r.Height));Bitmap thumb=new Bitmap(Math.Max(1,(int)(r.Width*ratio)),Math.Max(1,(int)(r.Height*ratio)));using(Graphics g=Graphics.FromImage(thumb)){g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(capture,new Rectangle(Point.Empty,thumb.Size),r,GraphicsUnit.Pixel);}return thumb;
        }
        protected override void Dispose(bool disposing){if(disposing)capture.Dispose();base.Dispose(disposing);}
    }

    internal static class Program
    {
        [STAThread] private static void Main(string[] args)
        {
            try{Native.SetProcessDPIAware();}catch{}
            string root=AppDomain.CurrentDomain.BaseDirectory;
            string eventName="Local\\SimplePopupGuardExit_"+root.ToLowerInvariant().GetHashCode();
            if(args.Contains("--exit")){try{using(EventWaitHandle request=EventWaitHandle.OpenExisting(eventName))request.Set();}catch{}return;}
            using(EventWaitHandle shutdown=new EventWaitHandle(false,EventResetMode.AutoReset,eventName))
            using(EventWaitHandle openRequest=new EventWaitHandle(false,EventResetMode.AutoReset,eventName+"_Open"))
            {
            bool created;using(Mutex mutex=new Mutex(true,"Local\\SimplePopupGuard_"+root.ToLowerInvariant().GetHashCode(),out created))
            {
                if(!created){openRequest.Set();return;}
                Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                try{using(Engine engine=new Engine(System.IO.Path.Combine(root,"data"))){engine.Start();Application.Run(new MainForm(engine,args.Contains("--autostart"),shutdown,openRequest));}}
                catch(Exception ex){MessageBox.Show("弹窗拦截无法启动："+ex.Message,"弹窗拦截");}
            }
            }
        }
    }
}

// Isolated native renderer. Reads a data-only XML layout; never loads an ERP assembly.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;

public sealed class RoundedPanel : Panel
{
    public int CornerRadius {get;set;}
    public Color BorderColor {get;set;}
    public int BorderWidth {get;set;}
    public RoundedPanel() {CornerRadius=20;BorderColor=Color.Gray;BorderWidth=1;}
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        var rect=new Rectangle(0,0,Width-1,Height-1);int d=CornerRadius*2;
        if(d<1||Width<2||Height<2) return;
        using(var path=new GraphicsPath()) using(var pen=new Pen(BorderColor,Math.Max(BorderWidth,1))) using(var brush=new SolidBrush(BackColor))
        {
            path.AddArc(rect.X,rect.Y,d,d,180,90);path.AddArc(rect.Right-d,rect.Y,d,d,270,90);
            path.AddArc(rect.Right-d,rect.Bottom-d,d,d,0,90);path.AddArc(rect.X,rect.Bottom-d,d,d,90,90);path.CloseFigure();
            e.Graphics.FillPath(brush,path);e.Graphics.DrawPath(pen,path);Region=new Region(path);
        }
    }
}
public static class ScreenRenderer
{
    private static object Create(string type)
    {
        switch(type)
        {
            case "Form":return new Form {FormBorderStyle=FormBorderStyle.None,AutoScaleMode=AutoScaleMode.None,ShowInTaskbar=false};
            case "GroupBox":return new GroupBox();case "RadioButton":return new RadioButton();case "ComboBox":case "MetroComboBox":return new ComboBox();case "LinkLabel":return new LinkLabel();case "RichTextBox":return new RichTextBox();case "TabControl":return new TabControl();case "TabPage":return new TabPage();case "ToolStrip":return new ToolStrip();case "ToolStripButton":return new ToolStripButton();case "Panel":return new Panel();case "PanelPersonalizado":return new RoundedPanel();
            case "Label":return new Label();case "TextBox":case "txtQtd":case "txtInteiro":case "txtValor":case "txtDataBranco":return new TextBox();case "Button":case "MetroButton":return new Button {FlatStyle=FlatStyle.Flat,BackColor=Color.White};
            case "PictureBox":return new PictureBox();case "DataGridView":case "MetroGrid":return new DataGridView {BackgroundColor=Color.White,AllowUserToAddRows=false,EnableHeadersVisualStyles=false};
            case "DataGridViewCellStyle":return new DataGridViewCellStyle();
            case "DataGridViewTextBoxColumn":return new DataGridViewTextBoxColumn();case "MenuStrip":return new MenuStrip();
            case "ToolStripMenuItem":return new ToolStripMenuItem();case "ToolStripSeparator":return new ToolStripSeparator();
            case "StatusStrip":return new StatusStrip();case "ToolStripStatusLabel":return new ToolStripStatusLabel();case "CheckBox":return new CheckBox();
            default:throw new InvalidOperationException("Unsupported control");
        }
    }
    [STAThread] public static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            XDocument doc;
            using(var reader=XmlReader.Create(args[0],new XmlReaderSettings {DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=2000000})) doc=XDocument.Load(reader);
            var nodes=doc.Root.Elements("node").ToArray();var objects=new Dictionary<string,object>();
            foreach(var node in nodes) objects.Add((string)node.Attribute("id"),Create((string)node.Attribute("type")));
            foreach(var node in nodes)
            {
                var obj=objects[(string)node.Attribute("id")];var control=obj as Control;if(control!=null) control.SuspendLayout();
                foreach(var p in node.Elements("property"))
                {
                    var property=obj.GetType().GetProperty((string)p.Attribute("name"),BindingFlags.Public|BindingFlags.Instance);
                    if(property==null||!property.CanWrite) continue;
                    try
                    {
                        string kind=(string)p.Attribute("kind"),value=p.Value;object parsed;
                        if(kind=="pair") {var parts=value.Split(',').Select(int.Parse).ToArray();parsed=property.PropertyType==typeof(Point)?(object)new Point(parts[0],parts[1]):new Size(parts[0],parts[1]);}
                        else if(kind=="color") {var rgb=value.Split(',');parsed=rgb.Length==1?(object)Color.FromName(value):rgb.Length==3?Color.FromArgb(int.Parse(rgb[0]),int.Parse(rgb[1]),int.Parse(rgb[2])):Color.FromArgb(int.Parse(rgb[0]),int.Parse(rgb[1]),int.Parse(rgb[2]),int.Parse(rgb[3]));}
                        else if(kind=="font") {var parts=value.Split('|');parsed=new Font(parts[0],float.Parse(parts[1],CultureInfo.InvariantCulture),(FontStyle)Enum.Parse(typeof(FontStyle),parts[2]));}
                        else if(property.PropertyType.IsEnum) parsed=Enum.Parse(property.PropertyType,value);
                        else parsed=Convert.ChangeType(value,property.PropertyType,CultureInfo.InvariantCulture);
                        property.SetValue(obj,parsed,null);
                    } catch(ArgumentException) { } catch(TargetInvocationException) { }
                }
                // Designer placeholders are not customer-facing data.
                if(control!=null&&control.Text==(string)node.Attribute("id")) control.Text="";
                var item=obj as ToolStripItem;if(item!=null&&item.Text==(string)node.Attribute("id")) item.Text="";
            }
            foreach(var node in nodes)
            {
                var parent=objects[(string)node.Attribute("id")];
                foreach(var reference in node.Elements("reference"))
                {var property=parent.GetType().GetProperty((string)reference.Attribute("name"));if(property!=null) property.SetValue(parent,objects[reference.Value],null);}
                foreach(var child in node.Elements("child"))
                {
                    var obj=objects[(string)child.Attribute("id")];
                    if(parent is DataGridView && obj is DataGridViewColumn) ((DataGridView)parent).Columns.Add((DataGridViewColumn)obj);
                    else if(parent is ToolStrip && obj is ToolStripItem) ((ToolStrip)parent).Items.Add((ToolStripItem)obj);
                    else if(parent is ToolStripMenuItem && obj is ToolStripItem) ((ToolStripMenuItem)parent).DropDownItems.Add((ToolStripItem)obj);
                    else if(parent is Control && obj is Control) ((Control)parent).Controls.Add((Control)obj);
                }
            }
            foreach(var control in objects.Values.OfType<Control>()) control.ResumeLayout(false);
            using(var form=(Form)objects["root"])
            {
                if(form.Width>4000||form.Height>4000||form.Width<1||form.Height<1) throw new InvalidOperationException("Invalid form dimensions");
                // Native controls need a visible parent to paint. Keep this isolated form fully transparent and off screen.
                form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-32000,-32000);form.Opacity=0;
                form.Show();form.PerformLayout();Application.DoEvents();
                object targetObject;Control targetControl=null;
                if(doc.Root.Attribute("target")!=null&&objects.TryGetValue((string)doc.Root.Attribute("target"),out targetObject)) targetControl=targetObject as Control;
                if(targetControl!=null)
                    for(Control ancestor=targetControl.Parent;ancestor!=null;ancestor=ancestor.Parent)
                        if(ancestor is TabPage&&ancestor.Parent is TabControl) ((TabControl)ancestor.Parent).SelectedTab=(TabPage)ancestor;
                form.PerformLayout();Application.DoEvents();
                var originalBounds=objects.Values.OfType<Control>().ToDictionary(c=>c,c=>c.Bounds);
                foreach(var control in objects.Values.OfType<Control>()) control.SuspendLayout();
                // Freeze the resolved docking geometry before compensating DrawToBitmap z-order.
                foreach(var control in objects.Values.OfType<Control>().Where(c=>c!=form)) {control.Dock=DockStyle.None;control.Anchor=AnchorStyles.None;}
                // DrawToBitmap paints child controls in reverse z-order; compensate only in this isolated renderer.
                foreach(var node in nodes)
                {
                    var parent=objects[(string)node.Attribute("id")] as Control;if(parent==null||parent is TabControl) continue;
                    int order=0;foreach(var child in node.Elements("child")) {var c=objects[(string)child.Attribute("id")] as Control;if(c!=null) parent.Controls.SetChildIndex(c,parent.Controls.Count-1-order++);}
                }
                foreach(var pair in originalBounds.Where(p=>p.Key!=form)) pair.Key.Bounds=pair.Value;
                foreach(var control in objects.Values.OfType<Control>()) control.ResumeLayout(false);
                if(args.Length>2) foreach(var pair in objects) {var c=pair.Value as Control;if(c!=null) Console.WriteLine(pair.Key+": "+c.Bounds+" Visible="+c.Visible);}
                using(var bitmap=new Bitmap(form.ClientSize.Width,form.ClientSize.Height)) {form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.ClientSize));bitmap.Save(args[1],ImageFormat.Png);}
                if(targetControl!=null&&targetControl.Visible)
                {
                    var origin=form.PointToScreen(Point.Empty);var location=targetControl.Parent.PointToScreen(targetControl.Location);
                    new XDocument(new XElement("bounds",new XAttribute("x",location.X-origin.X),new XAttribute("y",location.Y-origin.Y),new XAttribute("width",targetControl.Width),new XAttribute("height",targetControl.Height))).Save(args[1]+".bounds.xml");
                }
            }
            return 0;
        }
        catch(Exception ex) {Console.Error.WriteLine(ex.GetType().Name+": "+ex.Message);return 1;}
    }
}

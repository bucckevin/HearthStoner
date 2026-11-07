using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;

namespace HearthStoner
{
    /// <summary>
    /// The app's look: a deep indigo "night" base, vellum text, mana blue for anything you can act on, legendary
    /// gold for the single best thing on a screen. Barlow for text, Barlow Condensed for titles and numbers
    /// (both bundled in the exe). Control styles (scrollbars, buttons, toggles, segmented choices) are XAML
    /// templates parsed at startup.
    /// </summary>
    public static class Theme
    {
        public static readonly Color NightC = Rgb(0x12, 0x14, 0x28);
        public static readonly Color RailC = Rgb(0x0E, 0x10, 0x22);
        public static readonly Color SlateC = Rgb(0x1B, 0x1F, 0x3A);
        public static readonly Color RaisedC = Rgb(0x23, 0x28, 0x49);
        public static readonly Color LineC = Rgb(0x2C, 0x31, 0x58);
        public static readonly Color VellumC = Rgb(0xED, 0xE6, 0xD6);
        public static readonly Color DuskC = Rgb(0x8B, 0x90, 0xB8);
        public static readonly Color ManaC = Rgb(0x5A, 0xA0, 0xFF);
        public static readonly Color GoldC = Rgb(0xF0, 0xB9, 0x54);
        public static readonly Color WinC = Rgb(0x62, 0xD4, 0x8C);
        public static readonly Color LossC = Rgb(0xF0, 0x70, 0x6A);
        /// <summary>Small explanatory text: brighter than Dusk so it reads at caption sizes.</summary>
        public static readonly Color MistC = Rgb(0xA9, 0xAE, 0xD0);
        /// <summary>"Look at this": review notes, warnings, mistakes. Gold stays for rewards and rank.</summary>
        public static readonly Color AmberC = Rgb(0xFF, 0x9A, 0x4D);

        public static readonly Brush Night = B(NightC), Rail = B(RailC), Slate = B(SlateC), Raised = B(RaisedC), Line = B(LineC),
            Vellum = B(VellumC), Dusk = B(DuskC), Mana = B(ManaC), Gold = B(GoldC), Win = B(WinC), Loss = B(LossC),
            Mist = B(MistC), Amber = B(AmberC);

        /// <summary>Captions are never drawn smaller than this.</summary>
        public const double CaptionSize = 12.5;

        /// <summary>Line icon for a navigation page, on a 24-unit grid (drawn as a stroke).</summary>
        public static Geometry NavIcon(string page)
        {
            switch (page)
            {
                case "Play": return Geo("M8,5 L19,12 L8,19 Z");
                case "Decks": return Geo("M4,8 H15 V21 H4 Z M8,4 H19 V17");
                case "Ladder": return Geo("M3,19 L9,13 L13,16 L21,7 M16,7 H21 V12");
                case "History": return Geo("M12,3.5 A8.5,8.5 0 1 1 11.99,3.5 Z M12,7.5 V12 L15,14");
                case "Replays": return Geo("M3,5 H21 V19 H3 Z M10,9 L15,12 L10,15 Z");
                case "Practice": return Geo("M12,3.5 A8.5,8.5 0 1 1 11.99,3.5 Z M12,8.5 A3.5,3.5 0 1 1 11.99,8.5 Z");
                case "Stats": return Geo("M5,20 V13 M10,20 V5 M15,20 V10 M20,20 V15");
                case "Meta": return Geo("M12,3.5 A8.5,8.5 0 1 1 11.99,3.5 Z M3.5,12 H20.5 M12,3.5 C8,8 8,16 12,20.5 M12,3.5 C16,8 16,16 12,20.5");
                case "Settings": return Geo("M4,7 H20 M4,17 H20 M9,4 V10 M15,14 V20");
                default: return Geo("M12,4 L19,8 L19,16 L12,20 L5,16 L5,8 Z");
            }
        }

        private static Geometry Geo(string data) { var g = Geometry.Parse(data); g.Freeze(); return g; }

        private static readonly Uri FontBase = new Uri("pack://application:,,,/HearthStoner;component/");
        public static readonly FontFamily Body = new FontFamily(FontBase, "./App/Fonts/#Barlow");
        public static readonly FontFamily Display = new FontFamily(FontBase, "./App/Fonts/#Barlow Condensed");

        public static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
        public static SolidColorBrush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        public static SolidColorBrush B(Color c, double opacity) { var b = new SolidColorBrush(Color.FromArgb((byte)(255 * opacity), c.R, c.G, c.B)); b.Freeze(); return b; }

        /// <summary>A colour per class, tuned to read on the dark background.</summary>
        public static Color ClassColor(string cls)
        {
            switch ((cls ?? "").ToUpperInvariant())
            {
                case "DEATHKNIGHT": return Rgb(0x5D, 0xB7, 0xDB);
                case "DEMONHUNTER": return Rgb(0x4D, 0xB8, 0x66);
                case "DRUID": return Rgb(0xC0, 0x8A, 0x4E);
                case "HUNTER": return Rgb(0x8F, 0xC0, 0x4A);
                case "MAGE": return Rgb(0x6E, 0xA8, 0xFF);
                case "PALADIN": return Rgb(0xE8, 0xC1, 0x5A);
                case "PRIEST": return Rgb(0xD9, 0xD4, 0xC7);
                case "ROGUE": return Rgb(0x7A, 0x7F, 0x95);
                case "SHAMAN": return Rgb(0x3E, 0x7F, 0xF0);
                case "WARLOCK": return Rgb(0x9A, 0x6B, 0xE0);
                case "WARRIOR": return Rgb(0xD9, 0x5B, 0x4B);
                default: return Rgb(0x55, 0x5A, 0x80);
            }
        }

        /// <summary>Green above 50%, red below, plain in between.</summary>
        public static Brush RateBrush(double wr) => wr >= 0.52 ? Win : wr < 0.48 ? Loss : Vellum;

        // ------------------------------------------------------------------ text helpers

        /// <summary>
        /// A text block. Small grey text (Dusk under 13 px) is a caption: it is drawn in the brighter Mist, at least
        /// <see cref="CaptionSize"/>, so it stays readable on a big screen.
        /// </summary>
        public static TextBlock Text(string text, double size = 14, Brush fg = null, FontWeight? weight = null, bool display = false, bool wrap = false)
        {
            if (ReferenceEquals(fg, Dusk) && size < 13) { fg = Mist; size = Math.Max(size + 1, CaptionSize); }
            return new TextBlock
            {
                Text = text ?? "", FontSize = size, Foreground = fg ?? Vellum, FontWeight = weight ?? FontWeights.Normal,
                FontFamily = display ? Display : Body, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
                TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
            };
        }

        /// <summary>Page title in the display face.</summary>
        public static TextBlock Title(string text, double size = 38) => Text(text, size, Vellum, FontWeights.SemiBold, display: true);

        /// <summary>A section heading inside a page.</summary>
        public static TextBlock Heading(string text) { var t = Text(text, 20, Vellum, FontWeights.SemiBold, display: true); t.Margin = new Thickness(0, 0, 0, 10); return t; }

        public static Button Button(string text, Action click, bool primary = true)
        {
            var b = new Button { Content = text, Style = (Style)Styles[primary ? "Primary" : "Ghost"], HorizontalAlignment = HorizontalAlignment.Left };
            b.Click += (s, e) => click();
            return b;
        }

        public static Border Hairline(Thickness? margin = null) =>
            new Border { Height = 1, Background = Line, Margin = margin ?? new Thickness(0) };

        // ------------------------------------------------------------------ control styles

        private static ResourceDictionary _styles;
        public static ResourceDictionary Styles => _styles ?? (_styles = (ResourceDictionary)XamlReader.Parse(Xaml));

        private const string Xaml = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <SolidColorBrush x:Key='Line' Color='#2C3158'/>
  <SolidColorBrush x:Key='Mana' Color='#5AA0FF'/>
  <SolidColorBrush x:Key='Vellum' Color='#EDE6D6'/>
  <SolidColorBrush x:Key='Dusk' Color='#8B90B8'/>

  <!-- Thin scrollbar that sits quietly in the margin -->
  <Style TargetType='ScrollBar'>
    <Setter Property='Width' Value='10'/>
    <Setter Property='MinWidth' Value='10'/>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ScrollBar'>
          <Grid Background='Transparent'>
            <Track x:Name='PART_Track' IsDirectionReversed='True'>
              <Track.Thumb>
                <Thumb>
                  <Thumb.Template>
                    <ControlTemplate TargetType='Thumb'>
                      <Border x:Name='T' CornerRadius='3' Background='#3A4070' Margin='2,2,3,2'/>
                      <ControlTemplate.Triggers>
                        <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='T' Property='Background' Value='#5A6198'/></Trigger>
                      </ControlTemplate.Triggers>
                    </ControlTemplate>
                  </Thumb.Template>
                </Thumb>
              </Track.Thumb>
            </Track>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
    <Style.Triggers>
      <Trigger Property='Orientation' Value='Horizontal'>
        <Setter Property='Height' Value='0'/>
        <Setter Property='MinHeight' Value='0'/>
      </Trigger>
    </Style.Triggers>
  </Style>

  <Style x:Key='Primary' TargetType='Button'>
    <Setter Property='Foreground' Value='#0B1430'/>
    <Setter Property='Background' Value='#5AA0FF'/>
    <Setter Property='FontWeight' Value='SemiBold'/>
    <Setter Property='FontSize' Value='13.5'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Height' Value='34'/>
    <Setter Property='Padding' Value='16,0'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='B' CornerRadius='7' Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}'>
            <ContentPresenter VerticalAlignment='Center' HorizontalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='B' Property='Background' Value='#7BB4FF'/></Trigger>
            <Trigger Property='IsPressed' Value='True'><Setter TargetName='B' Property='Background' Value='#4A8AE6'/></Trigger>
            <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='B' Property='BorderBrush' Value='#EDE6D6'/><Setter TargetName='B' Property='BorderThickness' Value='1.5'/></Trigger>
            <Trigger Property='IsEnabled' Value='False'><Setter TargetName='B' Property='Opacity' Value='0.45'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Ghost' TargetType='Button' BasedOn='{StaticResource Primary}'>
    <Setter Property='Foreground' Value='#EDE6D6'/>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='B' CornerRadius='7' Background='{TemplateBinding Background}' BorderBrush='#2C3158' BorderThickness='1' Padding='{TemplateBinding Padding}'>
            <ContentPresenter VerticalAlignment='Center' HorizontalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='B' Property='BorderBrush' Value='#5AA0FF'/></Trigger>
            <Trigger Property='IsPressed' Value='True'><Setter TargetName='B' Property='Background' Value='#232849'/></Trigger>
            <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='B' Property='BorderBrush' Value='#EDE6D6'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Navigation item: mana crystal + label; the selected one glows -->
  <Style x:Key='Nav' TargetType='RadioButton'>
    <Setter Property='Foreground' Value='#8B90B8'/>
    <Setter Property='FontSize' Value='15.5'/>
    <Setter Property='FontWeight' Value='Medium'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='RadioButton'>
          <Grid x:Name='Row' Height='40' Background='Transparent'>
            <Border x:Name='Glow' Opacity='0'>
              <Border.Background>
                <LinearGradientBrush StartPoint='0,0' EndPoint='1,0'>
                  <GradientStop Color='#225AA0FF' Offset='0'/><GradientStop Color='#005AA0FF' Offset='0.85'/>
                </LinearGradientBrush>
              </Border.Background>
            </Border>
            <Border x:Name='Bar' Width='3' HorizontalAlignment='Left' Margin='0,7' CornerRadius='0,2,2,0' Background='#5AA0FF' Opacity='0'/>
            <StackPanel Orientation='Horizontal' Margin='22,0,0,0' VerticalAlignment='Center'>
              <Path x:Name='Gem' Width='24' Height='24' Stretch='None' Fill='{x:Null}' Stroke='#8B90B8' StrokeThickness='2'
                    StrokeLineJoin='Round' StrokeStartLineCap='Round' StrokeEndLineCap='Round'
                    Data='{Binding Tag, RelativeSource={RelativeSource TemplatedParent}}'>
                <Path.LayoutTransform><ScaleTransform ScaleX='0.75' ScaleY='0.75'/></Path.LayoutTransform>
              </Path>
              <ContentPresenter Margin='12,0,0,0' VerticalAlignment='Center'/>
            </StackPanel>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter Property='Foreground' Value='#EDE6D6'/><Setter TargetName='Gem' Property='Stroke' Value='#EDE6D6'/></Trigger>
            <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Row' Property='Background' Value='#1B1F3A'/></Trigger>
            <Trigger Property='IsChecked' Value='True'>
              <Setter Property='Foreground' Value='#EDE6D6'/>
              <Setter TargetName='Glow' Property='Opacity' Value='1'/>
              <Setter TargetName='Bar' Property='Opacity' Value='1'/>
              <Setter TargetName='Gem' Property='Stroke' Value='#5AA0FF'/>
              <Setter TargetName='Gem' Property='Effect'>
                <Setter.Value><DropShadowEffect Color='#5AA0FF' BlurRadius='8' ShadowDepth='0' Opacity='0.8'/></Setter.Value>
              </Setter>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- On/off switch -->
  <Style x:Key='Toggle' TargetType='CheckBox'>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='CheckBox'>
          <Grid Background='Transparent'>
            <Border x:Name='Track' Width='40' Height='22' CornerRadius='11' Background='#2C3158'/>
            <Border x:Name='Knob' Width='16' Height='16' CornerRadius='8' Background='#8B90B8' HorizontalAlignment='Left' Margin='3,0,0,0'/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='Track' Property='Background' Value='#5AA0FF'/>
              <Setter TargetName='Knob' Property='Background' Value='#0B1430'/>
              <Setter TargetName='Knob' Property='Margin' Value='21,0,0,0'/>
            </Trigger>
            <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Track' Property='BorderBrush' Value='#EDE6D6'/><Setter TargetName='Track' Property='BorderThickness' Value='1.5'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- One option of a segmented choice -->
  <Style x:Key='Segment' TargetType='RadioButton'>
    <Setter Property='Foreground' Value='#8B90B8'/>
    <Setter Property='FontSize' Value='13.5'/>
    <Setter Property='FontWeight' Value='Medium'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='RadioButton'>
          <Border x:Name='B' CornerRadius='6' Padding='12,6' Background='Transparent' Margin='2'>
            <ContentPresenter HorizontalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter Property='Foreground' Value='#EDE6D6'/></Trigger>
            <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='B' Property='BorderBrush' Value='#EDE6D6'/><Setter TargetName='B' Property='BorderThickness' Value='1'/></Trigger>
            <Trigger Property='IsChecked' Value='True'><Setter TargetName='B' Property='Background' Value='#2E3566'/><Setter Property='Foreground' Value='#EDE6D6'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='ToolTip'>
    <Setter Property='Background' Value='#232849'/>
    <Setter Property='Foreground' Value='#EDE6D6'/>
    <Setter Property='BorderBrush' Value='#2C3158'/>
    <Setter Property='Padding' Value='8,5'/>
  </Style>
</ResourceDictionary>";

        // ------------------------------------------------------------------ window caption

        /// <summary>Dark title bar in the rail's colour (Windows 11; ignored on older versions).</summary>
        public static void DarkCaption(Window w)
        {
            try
            {
                var hwnd = new WindowInteropHelper(w).Handle;
                var on = 1;
                DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int));                    // DWMWA_USE_IMMERSIVE_DARK_MODE
                var caption = RailC.R | (RailC.G << 8) | (RailC.B << 16);                    // COLORREF is 0x00BBGGRR
                DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int));               // DWMWA_CAPTION_COLOR
                var text = VellumC.R | (VellumC.G << 8) | (VellumC.B << 16);
                DwmSetWindowAttribute(hwnd, 36, ref text, sizeof(int));                  // DWMWA_TEXT_COLOR
            }
            catch (Exception swallowed) { Diag.Ignored(swallowed, "Theme"); }
        }

        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    }
}

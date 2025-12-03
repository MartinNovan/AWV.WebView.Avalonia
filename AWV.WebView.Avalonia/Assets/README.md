## AWV.WebView.Avalonia
- Cross-platform WebView control for Avalonia. Supports **Windows**, **Linux (GTK)**, and **Android**.
---
## Setup
### Windows
1. Make sure your project targets the **Windows-specific TFM** to support WinForms/WPF components:
```xml
<TargetFramework>net9.0-windows</TargetFramework>
<UseWindowsForms>true</UseWindowsForms>
```
- or you can add this to your csproj
```xml
<PropertyGroup Condition="'$(OS)' == 'Windows_NT'">
    <UseWindowsForms>true</UseWindowsForms>
    <TargetFramework>net9.0-windows</TargetFramework>
</PropertyGroup>
```
3. Then you can add the WebView in your `.axaml`:

```xml
<awv:WebView Url="https://www.github.com"/>
```
>[!NOTE]
> - Example project: [Sample Windows project .csproj](https://github.com/MartinNovan/AWV.WebView.Avalonia/blob/master/AWV.WebView.Avalonia.Sample.Windows/AWV.WebView.Avalonia.Sample.Windows.csproj)
> - Example usage in XAML: [MainView.axaml](https://github.com/MartinNovan/AWV.WebView.Avalonia/blob/master/AWV.WebView.Avalonia.Sample/Views/MainView.axaml)
---
### Linux
1. Ensure the following native dependencies are installed:
```bash
# libgtk-3 and libwebkit2gtk
sudo pacman -Syu gtk3 webkit2gtk
```
2. Target plain `net9.0` in your `.csproj`:
```xml
<TargetFramework>net9.0</TargetFramework>
```
3. Add the WebView in your XAML the same way:
```xml
<awv:WebView Url="https://www.github.com"/>
```
---
### Android
1. Target `net9.0-android` in your `.csproj`.
2. No additional setup is required; permissions are declared in the Android manifest.
3. Usage in XAML is the same as above.
---
## Dependencies
- **Linux**: `libgtk-3` and `libwebkit2gtk`
- **Windows**: WebView2 runtime
---
## Tested Platforms
### **Windows 11**
 ![Windows\_preview.png](https://github.com/MartinNovan/AWV.WebView.Avalonia/blob/master/Pictures/Windows_preview.png)
### **Arch Linux**
 ![Linux\_preview.png](https://github.com/MartinNovan/AWV.WebView.Avalonia/blob/master/Pictures/Linux_preview.png)
### **Android**
 ![Android\_preview.png](https://github.com/MartinNovan/AWV.WebView.Avalonia/blob/master/Pictures/Android_preview.png)
---
## Notes
- This project started as side project of my bigger project, so it might not be perfect.
- The control automatically picks the correct platform implementation.
- WindowsForms is required **only for Windows**. Using it on Linux/macOS will fail at build time.
- If you found any issues, or want some features to add, please report them on the [Issues page](https://github.com/MartinNovan/AWV.WebView.Avalonia/issues).
---
## License

- This project is under [GNU GENERAL PUBLIC LICENSE Version 3](https://github.com/MartinNovan/AWV.WebView.Avalonia/blob/master/LICENSE).

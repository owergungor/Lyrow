using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Lyrow.App.Platform;

/// <summary>
/// Manages the Windows System Tray / Notification Area icon and its native context menu.
/// </summary>
public sealed class LyrowTrayIconManager : IDisposable
{
    private TrayIcon? _trayIcon;
    private NativeMenuItem? _itemLumn;
    private NativeMenuItem? _itemVerci;
    private NativeMenuItem? _itemClickThrough;
    private bool _isDisposed;

    public bool IsVisible => _trayIcon?.IsVisible ?? false;

    public void Initialize(
        Action onShowMainWindow,
        Action onToggleLumn,
        Action onToggleVerci,
        Action onToggleClickThrough,
        Action onOpenSettings,
        Action onExit)
    {
        if (_trayIcon != null) return;

        try
        {
            var menu = new NativeMenu();

            var itemShow = new NativeMenuItem("Lyrow'u Göster");
            itemShow.Click += (_, _) => onShowMainWindow();
            menu.Add(itemShow);

            menu.Add(new NativeMenuItemSeparator());

            _itemLumn = new NativeMenuItem("Glow Görselleştirici (Aç)");
            _itemLumn.Click += (_, _) => onToggleLumn();
            menu.Add(_itemLumn);

            _itemVerci = new NativeMenuItem("Lyrics Şarkı Sözleri (Aç)");
            _itemVerci.Click += (_, _) => onToggleVerci();
            menu.Add(_itemVerci);

            _itemClickThrough = new NativeMenuItem("Click-Through (Kapalı)");
            _itemClickThrough.Click += (_, _) => onToggleClickThrough();
            menu.Add(_itemClickThrough);

            menu.Add(new NativeMenuItemSeparator());

            var itemSettings = new NativeMenuItem("Ayarlar");
            itemSettings.Click += (_, _) => onOpenSettings();
            menu.Add(itemSettings);

            menu.Add(new NativeMenuItemSeparator());

            var itemExit = new NativeMenuItem("Çıkış");
            itemExit.Click += (_, _) => onExit();
            menu.Add(itemExit);

            _trayIcon = new TrayIcon
            {
                ToolTipText = "Lyrow v1.1 — Multimedya Deneyim Merkezi",
                Menu = menu,
                IsVisible = true
            };

            try
            {
                using var stream = AssetLoader.Open(new Uri("avares://Lyrow.App/Assets/lyrow.ico"));
                _trayIcon.Icon = new WindowIcon(stream);
            }
            catch
            {
                // Fallback to default icon if stream not found
            }

            _trayIcon.Clicked += (_, _) => onShowMainWindow();

            if (Application.Current != null)
            {
                var trayIcons = TrayIcon.GetIcons(Application.Current);
                if (trayIcons == null)
                {
                    trayIcons = new TrayIcons();
                    TrayIcon.SetIcons(Application.Current, trayIcons);
                }
                trayIcons.Add(_trayIcon);
            }
        }
        catch
        {
            // System tray may not be available on headless or non-desktop environments
        }
    }

    public void UpdateStates(bool lumnOpen, bool verciOpen, bool clickThroughActive)
    {
        if (_itemLumn != null)
        {
            _itemLumn.Header = lumnOpen ? "Glow Görselleştirici (Kapat)" : "Glow Görselleştirici (Aç)";
        }

        if (_itemVerci != null)
        {
            _itemVerci.Header = verciOpen ? "Lyrics Şarkı Sözleri (Kapat)" : "Lyrics Şarkı Sözleri (Aç)";
        }

        if (_itemClickThrough != null)
        {
            _itemClickThrough.Header = clickThroughActive ? "Click-Through: Aktif" : "Click-Through: Pasif";
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (_trayIcon != null)
        {
            _trayIcon.IsVisible = false;
            if (Application.Current != null)
            {
                var trayIcons = TrayIcon.GetIcons(Application.Current);
                trayIcons?.Remove(_trayIcon);
            }
            _trayIcon.Dispose();
            _trayIcon = null;
        }
    }
}

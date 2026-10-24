using System.Globalization;

namespace Lyrow.Core.Localization;

/// <summary>
/// Lightweight multi-language localization manager supporting English, Français, Español, Deutsch, and Türkçe.
/// Automatically detects Windows UI locale or respects user preference with runtime dynamic switching.
/// </summary>
public sealed class LocalizationService
{
    public static readonly LocalizationService Instance = new();

    public static readonly IReadOnlyList<(string Code, string NativeName, string EnglishName)> SupportedLanguages = new[]
    {
        ("en", "English", "English"),
        ("fr", "Français", "French"),
        ("es", "Español", "Spanish"),
        ("de", "Deutsch", "German"),
        ("tr", "Türkçe", "Turkish")
    };

    private string _currentLanguage = "en";

    public event EventHandler<string>? LanguageChanged;

    public string CurrentLanguage
    {
        get => _currentLanguage;
        set => SetLanguage(value);
    }

    public LocalizationService()
    {
        _currentLanguage = DetectInitialLanguage();
    }

    public static string DetectInitialLanguage()
    {
        try
        {
            var twoLetter = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
            if (SupportedLanguages.Any(l => l.Code == twoLetter))
            {
                return twoLetter;
            }
        }
        catch { }

        return "en";
    }

    public bool SetLanguage(string langCode)
    {
        if (string.IsNullOrWhiteSpace(langCode)) return false;
        var clean = langCode.Trim().ToLowerInvariant();

        if (!SupportedLanguages.Any(l => l.Code == clean))
        {
            clean = "en";
        }

        if (_currentLanguage != clean)
        {
            _currentLanguage = clean;
            LanguageChanged?.Invoke(this, _currentLanguage);
            return true;
        }

        return false;
    }

    public string GetString(string key)
    {
        if (Translations.TryGetValue(_currentLanguage, out var dict) && dict.TryGetValue(key, out var val))
        {
            return val;
        }

        if (Translations.TryGetValue("en", out var enDict) && enDict.TryGetValue(key, out var fallback))
        {
            return fallback;
        }

        return key;
    }

    public string this[string key] => GetString(key);

    private static readonly Dictionary<string, Dictionary<string, string>> Translations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Nav_Home"] = "Home",
            ["Nav_Playback"] = "Playback",
            ["Nav_Glow"] = "Glow",
            ["Nav_Lyrics"] = "Lyrics",
            ["Nav_Settings"] = "Settings",

            ["App_Subtitle"] = "Windows 11 Ambient Edge Lighting & Apple Music-Style Synchronized Lyrics",
            ["Status_WaitingMedia"] = "Waiting for playback...",
            ["Status_NoSession"] = "No active media session",
            ["Status_PlayInApp"] = "Play music in Spotify, Apple Music, or your browser",
            ["Status_SearchingLyrics"] = "Searching for synchronized lyrics...",

            ["Settings_Title"] = "Settings",
            ["Tab_General"] = "General",
            ["Tab_Playback"] = "Playback",
            ["Tab_Lyrics"] = "Lyrics",
            ["Tab_Glow"] = "Glow",
            ["Tab_Updates"] = "Updates",
            ["Tab_Shortcuts"] = "Shortcuts",

            ["General_AppTheme"] = "App Theme",
            ["General_AppTheme_Desc"] = "Choose Windows 11 system theme, dark, or light appearance",
            ["General_Language"] = "Language",
            ["General_Language_Desc"] = "Application user interface language",
            ["General_Startup"] = "Launch on Windows Startup",
            ["General_Startup_Desc"] = "Start Lyrow automatically when signing into Windows",
            ["General_SystemTray"] = "System Tray Integration",
            ["General_SystemTray_Desc"] = "Keep Lyrow accessible in the Windows notification area",

            ["Playback_Title"] = "Playback & Volume",
            ["Playback_ActiveSession"] = "Active Session Playback Volume",
            ["Playback_SystemEndpoint"] = "System Endpoint Master Volume",
            ["Playback_Transport"] = "Media Transport Controls",
            ["Playback_Muted"] = "Muted",

            ["Lyrics_Title"] = "Lyrics",
            ["Lyrics_SyncOffset"] = "Synchronization Offset",
            ["Lyrics_SyncOffset_Desc"] = "Adjust timing offset in milliseconds",
            ["Lyrics_FocusedFontSize"] = "Focused Line Font Size",
            ["Lyrics_FocusedFontSize_Desc"] = "Font size of active lyric line (20–48 px)",
            ["Lyrics_UnfocusedFontSize"] = "Unfocused Lines Font Size",
            ["Lyrics_UnfocusedFontSize_Desc"] = "Font size of inactive lyric lines (14–36 px)",
            ["Lyrics_AutoCenter_Notice"] = "Apple Music fluid motion and vertical centering are always enabled.",
            ["Lyrics_SeekFailed"] = "Seeking is not supported by this media player",

            ["Glow_Title"] = "Glow",
            ["Glow_AmbientGlow"] = "Ambient Edge Glow",
            ["Glow_AmbientGlow_Desc"] = "Toggle 4-edge music-reactive ambient lighting",
            ["Glow_Intensity"] = "Glow Intensity",
            ["Glow_ArtworkSync"] = "Artwork Color Adaptation",
            ["Glow_ArtworkSync_Desc"] = "Smoothly crossfade edge colors to match current track artwork",
            ["Glow_ShutdownDelay"] = "Inactivity Shutdown Delay",
            ["Glow_ShutdownDelay_Desc"] = "Graceful fade-out timer when playback stops",

            ["Updates_Title"] = "Updates",
            ["Updates_AutoChecks"] = "Automatic Update Checks",
            ["Updates_AutoChecks_Desc"] = "Check GitHub Releases in the background without interrupting playback",
            ["Updates_Frequency"] = "Check Frequency",
            ["Updates_CurrentVersion"] = "Current Version",
            ["Updates_Channel"] = "Release channel: GitHub Releases (Stable)",
            ["Updates_CheckNow"] = "Check for Updates",
            ["Updates_DownloadAndInstall"] = "Download and Install",
            ["Updates_Downloading"] = "Downloading update...",
            ["Updates_InstallPrompt"] = "Update downloaded. Install and restart Lyrow now?",
            ["Updates_InstallBtn"] = "Install & Restart",
            ["Updates_UpToDate"] = "Lyrow is up to date.",
            ["Updates_Available"] = "Update available",
            ["Updates_Preparing"] = "Preparing update...",
            ["Updates_Installing"] = "Installing update...",
            ["Updates_Complete"] = "Update complete",
            ["Updates_Failed"] = "Update failed",
            ["Updates_Retry"] = "Retry",

            ["Shortcuts_Title"] = "Shortcuts",
            ["Shortcuts_Desc"] = "User-defined global hotkeys. No default hotkeys are forced to prevent collisions.",
            ["Shortcuts_ToggleGlow"] = "Toggle Glow:",
            ["Shortcuts_ToggleLyrics"] = "Toggle Lyrics:",
            ["Shortcuts_Clear"] = "Clear",
            ["Shortcuts_Apply"] = "Apply Shortcuts",

            ["Diag_ResetSettings"] = "Reset Settings to Defaults",
            ["Diag_OpenLogs"] = "Open Diagnostic Log"
        },

        ["fr"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Nav_Home"] = "Accueil",
            ["Nav_Playback"] = "Lecture",
            ["Nav_Glow"] = "Lueur",
            ["Nav_Lyrics"] = "Paroles",
            ["Nav_Settings"] = "Paramètres",

            ["App_Subtitle"] = "Éclairage d'ambiance périphérique et paroles synchronisées style Apple Music pour Windows 11",
            ["Status_WaitingMedia"] = "En attente de lecture...",
            ["Status_NoSession"] = "Aucune session multimédia active",
            ["Status_PlayInApp"] = "Lancez de la musique sur Spotify, Apple Music ou votre navigateur",
            ["Status_SearchingLyrics"] = "Recherche des paroles synchronisées...",

            ["Settings_Title"] = "Paramètres",
            ["Tab_General"] = "Général",
            ["Tab_Playback"] = "Lecture",
            ["Tab_Lyrics"] = "Paroles",
            ["Tab_Glow"] = "Lueur",
            ["Tab_Updates"] = "Mises à jour",
            ["Tab_Shortcuts"] = "Raccourcis",

            ["General_AppTheme"] = "Thème de l'application",
            ["General_AppTheme_Desc"] = "Choisir le thème Windows 11 par défaut, sombre ou clair",
            ["General_Language"] = "Langue",
            ["General_Language_Desc"] = "Langue de l'interface utilisateur de l'application",
            ["General_Startup"] = "Lancer au démarrage de Windows",
            ["General_Startup_Desc"] = "Démarrer Lyrow automatiquement à l'ouverture de session",
            ["General_SystemTray"] = "Intégration dans la barre des tâches",
            ["General_SystemTray_Desc"] = "Garder Lyrow accessible dans la zone de notification Windows",

            ["Playback_Title"] = "Lecture et volume",
            ["Playback_ActiveSession"] = "Volume de la session active",
            ["Playback_SystemEndpoint"] = "Volume principal du système",
            ["Playback_Transport"] = "Commandes multimédias",
            ["Playback_Muted"] = "Muet",

            ["Lyrics_Title"] = "Paroles",
            ["Lyrics_SyncOffset"] = "Décalage de synchronisation",
            ["Lyrics_SyncOffset_Desc"] = "Ajuster le décalage temporel en millisecondes",
            ["Lyrics_FocusedFontSize"] = "Taille du texte actif",
            ["Lyrics_FocusedFontSize_Desc"] = "Taille de la ligne de paroles en cours (20–48 px)",
            ["Lyrics_UnfocusedFontSize"] = "Taille du texte inactif",
            ["Lyrics_UnfocusedFontSize_Desc"] = "Taille des lignes inactives (14–36 px)",
            ["Lyrics_AutoCenter_Notice"] = "L'animation fluide et le centrage vertical sont toujours activés.",
            ["Lyrics_SeekFailed"] = "La navigation temporelle n'est pas supportée par ce lecteur",

            ["Glow_Title"] = "Lueur",
            ["Glow_AmbientGlow"] = "Lueur ambiante sur les bords",
            ["Glow_AmbientGlow_Desc"] = "Activer l'éclairage ambiant réactif sur les 4 bords",
            ["Glow_Intensity"] = "Intensité de la lueur",
            ["Glow_ArtworkSync"] = "Adaptation aux couleurs de l'album",
            ["Glow_ArtworkSync_Desc"] = "Fondu enchaîné des couleurs selon la pochette du morceau",
            ["Glow_ShutdownDelay"] = "Délai d'extinction après arrêt",
            ["Glow_ShutdownDelay_Desc"] = "Délai d'estompage progressif à l'arrêt de la lecture",

            ["Updates_Title"] = "Mises à jour",
            ["Updates_AutoChecks"] = "Vérification automatique",
            ["Updates_AutoChecks_Desc"] = "Vérifier les versions GitHub en arrière-plan sans interruption",
            ["Updates_Frequency"] = "Fréquence de vérification",
            ["Updates_CurrentVersion"] = "Version actuelle",
            ["Updates_Channel"] = "Canal de diffusion : GitHub Releases (Stable)",
            ["Updates_CheckNow"] = "Rechercher des mises à jour",
            ["Updates_DownloadAndInstall"] = "Télécharger et installer",
            ["Updates_Downloading"] = "Téléchargement de la mise à jour...",
            ["Updates_InstallPrompt"] = "Mise à jour prête. Installer et redémarrer Lyrow maintenant ?",
            ["Updates_InstallBtn"] = "Installer et redémarrer",
            ["Updates_UpToDate"] = "Lyrow est à jour.",
            ["Updates_Available"] = "Mise à jour disponible",
            ["Updates_Preparing"] = "Préparation de la mise à jour...",
            ["Updates_Installing"] = "Installation de la mise à jour...",
            ["Updates_Complete"] = "Mise à jour terminée",
            ["Updates_Failed"] = "Échec de la mise à jour",
            ["Updates_Retry"] = "Réessayer",

            ["Shortcuts_Title"] = "Raccourcis",
            ["Shortcuts_Desc"] = "Raccourcis clavier personnalisés. Aucun conflit de raccourcis imposé.",
            ["Shortcuts_ToggleGlow"] = "Activer la lueur :",
            ["Shortcuts_ToggleLyrics"] = "Activer les paroles :",
            ["Shortcuts_Clear"] = "Effacer",
            ["Shortcuts_Apply"] = "Appliquer",

            ["Diag_ResetSettings"] = "Réinitialiser les paramètres par défaut",
            ["Diag_OpenLogs"] = "Ouvrir le journal de diagnostic"
        },

        ["es"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Nav_Home"] = "Inicio",
            ["Nav_Playback"] = "Reproducción",
            ["Nav_Glow"] = "Resplandor",
            ["Nav_Lyrics"] = "Letras",
            ["Nav_Settings"] = "Ajustes",

            ["App_Subtitle"] = "Iluminación ambiental en bordes y letras sincronizadas estilo Apple Music para Windows 11",
            ["Status_WaitingMedia"] = "Esperando reproducción...",
            ["Status_NoSession"] = "Sin sesión multimedia activa",
            ["Status_PlayInApp"] = "Reproduce música en Spotify, Apple Music o tu navegador",
            ["Status_SearchingLyrics"] = "Buscando letras sincronizadas...",

            ["Settings_Title"] = "Ajustes",
            ["Tab_General"] = "General",
            ["Tab_Playback"] = "Reproducción",
            ["Tab_Lyrics"] = "Letras",
            ["Tab_Glow"] = "Resplandor",
            ["Tab_Updates"] = "Actualizaciones",
            ["Tab_Shortcuts"] = "Atajos",

            ["General_AppTheme"] = "Tema de la aplicación",
            ["General_AppTheme_Desc"] = "Selecciona el tema de Windows 11, oscuro o claro",
            ["General_Language"] = "Idioma",
            ["General_Language_Desc"] = "Idioma de la interfaz de la aplicación",
            ["General_Startup"] = "Iniciar con Windows",
            ["General_Startup_Desc"] = "Iniciar Lyrow automáticamente al iniciar sesión en Windows",
            ["General_SystemTray"] = "Bandeja del sistema",
            ["General_SystemTray_Desc"] = "Mantener Lyrow accesible en el área de notificación",

            ["Playback_Title"] = "Reproducción y volumen",
            ["Playback_ActiveSession"] = "Volumen de la sesión activa",
            ["Playback_SystemEndpoint"] = "Volumen maestro del sistema",
            ["Playback_Transport"] = "Controles multimedia",
            ["Playback_Muted"] = "Silenciado",

            ["Lyrics_Title"] = "Letras",
            ["Lyrics_SyncOffset"] = "Desfase de sincronización",
            ["Lyrics_SyncOffset_Desc"] = "Ajustar desfase temporal en milisegundos",
            ["Lyrics_FocusedFontSize"] = "Tamaño de letra activa",
            ["Lyrics_FocusedFontSize_Desc"] = "Tamaño de la línea actual (20–48 px)",
            ["Lyrics_UnfocusedFontSize"] = "Tamaño de letras secundarias",
            ["Lyrics_UnfocusedFontSize_Desc"] = "Tamaño de las líneas inactivas (14–36 px)",
            ["Lyrics_AutoCenter_Notice"] = "El movimiento fluido y centrado vertical están siempre activos.",
            ["Lyrics_SeekFailed"] = "El reproductor no admite desplazamiento temporal",

            ["Glow_Title"] = "Resplandor",
            ["Glow_AmbientGlow"] = "Resplandor ambiental en bordes",
            ["Glow_AmbientGlow_Desc"] = "Activar iluminación ambiental reactiva en los 4 bordes",
            ["Glow_Intensity"] = "Intensidad del resplandor",
            ["Glow_ArtworkSync"] = "Adaptación al color de portada",
            ["Glow_ArtworkSync_Desc"] = "Transición suave de colores según la carátula de la pista",
            ["Glow_ShutdownDelay"] = "Retardo de apagado por inactividad",
            ["Glow_ShutdownDelay_Desc"] = "Tiempo de desvanecimiento suave al detenerse la música",

            ["Updates_Title"] = "Actualizaciones",
            ["Updates_AutoChecks"] = "Comprobación automática",
            ["Updates_AutoChecks_Desc"] = "Buscar actualizaciones en GitHub en segundo plano sin interrumpir",
            ["Updates_Frequency"] = "Frecuencia de comprobación",
            ["Updates_CurrentVersion"] = "Versión actual",
            ["Updates_Channel"] = "Canal de lanzamiento: GitHub Releases (Estable)",
            ["Updates_CheckNow"] = "Buscar actualizaciones",
            ["Updates_DownloadAndInstall"] = "Descargar e instalar",
            ["Updates_Downloading"] = "Descargando actualización...",
            ["Updates_InstallPrompt"] = "Actualización descargada. ¿Instalar y reiniciar Lyrow ahora?",
            ["Updates_InstallBtn"] = "Instalar y reiniciar",
            ["Updates_UpToDate"] = "Lyrow está actualizado.",
            ["Updates_Available"] = "Actualización disponible",
            ["Updates_Preparing"] = "Preparando actualización...",
            ["Updates_Installing"] = "Instalando actualización...",
            ["Updates_Complete"] = "Actualización completada",
            ["Updates_Failed"] = "Actualización fallida",
            ["Updates_Retry"] = "Reintentar",

            ["Shortcuts_Title"] = "Atajos",
            ["Shortcuts_Desc"] = "Atajos globales definidos por el usuario sin colisiones forzadas.",
            ["Shortcuts_ToggleGlow"] = "Alternar resplandor:",
            ["Shortcuts_ToggleLyrics"] = "Alternar letras:",
            ["Shortcuts_Clear"] = "Limpiar",
            ["Shortcuts_Apply"] = "Aplicar atajos",

            ["Diag_ResetSettings"] = "Restablecer ajustes por defecto",
            ["Diag_OpenLogs"] = "Abrir registro de diagnóstico"
        },

        ["de"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Nav_Home"] = "Startseite",
            ["Nav_Playback"] = "Wiedergabe",
            ["Nav_Glow"] = "Glow",
            ["Nav_Lyrics"] = "Songtexte",
            ["Nav_Settings"] = "Einstellungen",

            ["App_Subtitle"] = "Windows 11 Ambient-Randbeleuchtung & Apple Music-Songtexte",
            ["Status_WaitingMedia"] = "Warte auf Wiedergabe...",
            ["Status_NoSession"] = "Keine aktive Mediensitzung",
            ["Status_PlayInApp"] = "Spielen Sie Musik in Spotify, Apple Music oder im Browser ab",
            ["Status_SearchingLyrics"] = "Suche synchronisierte Songtexte...",

            ["Settings_Title"] = "Einstellungen",
            ["Tab_General"] = "Allgemein",
            ["Tab_Playback"] = "Wiedergabe",
            ["Tab_Lyrics"] = "Songtexte",
            ["Tab_Glow"] = "Glow",
            ["Tab_Updates"] = "Updates",
            ["Tab_Shortcuts"] = "Tastenkürzel",

            ["General_AppTheme"] = "App-Design",
            ["General_AppTheme_Desc"] = "Windows 11-Systemdesign, Dunkel oder Hell wählen",
            ["General_Language"] = "Sprache",
            ["General_Language_Desc"] = "Sprache der Benutzeroberfläche",
            ["General_Startup"] = "Mit Windows starten",
            ["General_Startup_Desc"] = "Lyrow automatisch bei der Windows-Anmeldung starten",
            ["General_SystemTray"] = "System-Tray-Integration",
            ["General_SystemTray_Desc"] = "Lyrow im Windows-Benachrichtigungsbereich verfügbar halten",

            ["Playback_Title"] = "Wiedergabe & Lautstärke",
            ["Playback_ActiveSession"] = "Lautstärke der aktiven Sitzung",
            ["Playback_SystemEndpoint"] = "Gesamtlautstärke des Systems",
            ["Playback_Transport"] = "Mediensteuerung",
            ["Playback_Muted"] = "Stummgeschaltet",

            ["Lyrics_Title"] = "Songtexte",
            ["Lyrics_SyncOffset"] = "Synchronisations-Offset",
            ["Lyrics_SyncOffset_Desc"] = "Zeitversatz in Millisekunden anpassen",
            ["Lyrics_FocusedFontSize"] = "Schriftgröße aktive Zeile",
            ["Lyrics_FocusedFontSize_Desc"] = "Schriftgröße der aktuellen Textzeile (20–48 px)",
            ["Lyrics_UnfocusedFontSize"] = "Schriftgröße inaktive Zeilen",
            ["Lyrics_UnfocusedFontSize_Desc"] = "Schriftgröße der übrigen Textzeilen (14–36 px)",
            ["Lyrics_AutoCenter_Notice"] = "Flüssige Bewegung und vertikale Zentrierung sind dauerhaft aktiv.",
            ["Lyrics_SeekFailed"] = "Spulen wird von diesem Player nicht unterstützt",

            ["Glow_Title"] = "Glow",
            ["Glow_AmbientGlow"] = "Ambient-Randbeleuchtung",
            ["Glow_AmbientGlow_Desc"] = "Musikreaktive 4-Kanten-Hintergrundbeleuchtung aktivieren",
            ["Glow_Intensity"] = "Glow-Intensität",
            ["Glow_ArtworkSync"] = "Album-Farbabstimmung",
            ["Glow_ArtworkSync_Desc"] = "Sanfter Farbübergang passend zum aktuellen Album-Cover",
            ["Glow_ShutdownDelay"] = "Ausschaltverzögerung bei Inaktivität",
            ["Glow_ShutdownDelay_Desc"] = "Sanfte Ausblenddauer, wenn die Wiedergabe stoppt",

            ["Updates_Title"] = "Updates",
            ["Updates_AutoChecks"] = "Automatische Updatesuche",
            ["Updates_AutoChecks_Desc"] = "GitHub Releases im Hintergrund prüfen ohne Wiedergabeunterbrechung",
            ["Updates_Frequency"] = "Prüfhäufigkeit",
            ["Updates_CurrentVersion"] = "Aktuelle Version",
            ["Updates_Channel"] = "Release-Kanal: GitHub Releases (Stabil)",
            ["Updates_CheckNow"] = "Nach Updates suchen",
            ["Updates_DownloadAndInstall"] = "Herunterladen und installieren",
            ["Updates_Downloading"] = "Update wird heruntergeladen...",
            ["Updates_InstallPrompt"] = "Update heruntergeladen. Jetzt installieren und Lyrow neu starten?",
            ["Updates_InstallBtn"] = "Installieren & Neu starten",
            ["Updates_UpToDate"] = "Lyrow ist auf dem neuesten Stand.",
            ["Updates_Available"] = "Update verfügbar",
            ["Updates_Preparing"] = "Update wird vorbereitet...",
            ["Updates_Installing"] = "Update wird installiert...",
            ["Updates_Complete"] = "Update abgeschlossen",
            ["Updates_Failed"] = "Update fehlgeschlagen",
            ["Updates_Retry"] = "Wiederholen",

            ["Shortcuts_Title"] = "Tastenkürzel",
            ["Shortcuts_Desc"] = "Benutzerdefinierte Hotkeys ohne erzwungene Tastenkollisionen.",
            ["Shortcuts_ToggleGlow"] = "Glow umschalten:",
            ["Shortcuts_ToggleLyrics"] = "Songtexte umschalten:",
            ["Shortcuts_Clear"] = "Löschen",
            ["Shortcuts_Apply"] = "Tastenkürzel anwenden",

            ["Diag_ResetSettings"] = "Einstellungen auf Standard zurücksetzen",
            ["Diag_OpenLogs"] = "Diagnoseprotokoll öffnen"
        },

        ["tr"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Nav_Home"] = "Ana Sayfa",
            ["Nav_Playback"] = "Oynatma",
            ["Nav_Glow"] = "Işık (Glow)",
            ["Nav_Lyrics"] = "Şarkı Sözleri",
            ["Nav_Settings"] = "Ayarlar",

            ["App_Subtitle"] = "Windows 11 Ortam Kenar Aydınlatması ve Apple Music Tarzı Senkronize Şarkı Sözleri",
            ["Status_WaitingMedia"] = "Oynatma bekleniyor...",
            ["Status_NoSession"] = "Aktif medya oturumu yok",
            ["Status_PlayInApp"] = "Spotify, Apple Music veya tarayıcınızdan bir parça çalın",
            ["Status_SearchingLyrics"] = "Senkronize şarkı sözleri aranıyor...",

            ["Settings_Title"] = "Ayarlar",
            ["Tab_General"] = "Genel",
            ["Tab_Playback"] = "Oynatma",
            ["Tab_Lyrics"] = "Şarkı Sözleri",
            ["Tab_Glow"] = "Glow",
            ["Tab_Updates"] = "Güncellemeler",
            ["Tab_Shortcuts"] = "Kısayollar",

            ["General_AppTheme"] = "Uygulama Teması",
            ["General_AppTheme_Desc"] = "Windows 11 sistem varsayılanı, koyu veya açık görünüm seçin",
            ["General_Language"] = "Dil",
            ["General_Language_Desc"] = "Uygulama kullanıcı arayüzü dili",
            ["General_Startup"] = "Windows Başlangıcında Çalıştır",
            ["General_Startup_Desc"] = "Windows oturumu açıldığında Lyrow'u otomatik başlat",
            ["General_SystemTray"] = "Sistem Tepsisi Entegrasyonu",
            ["General_SystemTray_Desc"] = "Lyrow'u bildirim alanında arka planda erişilebilir tut",

            ["Playback_Title"] = "Oynatma ve Ses Seviyesi",
            ["Playback_ActiveSession"] = "Aktif Oturum Ses Seviyesi",
            ["Playback_SystemEndpoint"] = "Sistem Ana Çıkış Ses Seviyesi",
            ["Playback_Transport"] = "Medya Kontrolleri",
            ["Playback_Muted"] = "Sessize Alındı",

            ["Lyrics_Title"] = "Şarkı Sözleri",
            ["Lyrics_SyncOffset"] = "Zamanlama / Senkron Kayması",
            ["Lyrics_SyncOffset_Desc"] = "Milisaniye cinsinden zamanlama kaymasını ayarlayın",
            ["Lyrics_FocusedFontSize"] = "Aktif Söz Yazı Boyutu",
            ["Lyrics_FocusedFontSize_Desc"] = "Çalan satırın yazı boyutu (20–48 px)",
            ["Lyrics_UnfocusedFontSize"] = "Diğer Satırların Yazı Boyutu",
            ["Lyrics_UnfocusedFontSize_Desc"] = "İnaktif satırların yazı boyutu (14–36 px)",
            ["Lyrics_AutoCenter_Notice"] = "Akıcı animasyon ve dikey merkezleme her zaman aktiftir.",
            ["Lyrics_SeekFailed"] = "Bu medya oynatıcı sarma/konum değiştirmeyi desteklemiyor",

            ["Glow_Title"] = "Glow (Ortam Aydınlatması)",
            ["Glow_AmbientGlow"] = "Ortam Kenar Aydınlatması",
            ["Glow_AmbientGlow_Desc"] = "Müziğe duyarlı 4 ekran kenarı aydınlatmasını aç/kapat",
            ["Glow_Intensity"] = "Glow Parlaklık Yoğunluğu",
            ["Glow_ArtworkSync"] = "Albüm Kapağı Renk Uyumu",
            ["Glow_ArtworkSync_Desc"] = "Kenar renklerini çalan şarkının albüm kapağına yumuşakça uyarla",
            ["Glow_ShutdownDelay"] = "Hareketsizlik Sönme Gecikmesi",
            ["Glow_ShutdownDelay_Desc"] = "Müzik durduğunda yumuşak sönme süresi",

            ["Updates_Title"] = "Güncellemeler",
            ["Updates_AutoChecks"] = "Otomatik Güncelleme Kontrolü",
            ["Updates_AutoChecks_Desc"] = "Müzik akışını bölmeden GitHub Releases üzerinden güncellemeleri denetle",
            ["Updates_Frequency"] = "Kontrol Sıklığı",
            ["Updates_CurrentVersion"] = "Mevcut Sürüm",
            ["Updates_Channel"] = "Yayım kanalı: GitHub Releases (Kararlı)",
            ["Updates_CheckNow"] = "Güncellemeleri Denetle",
            ["Updates_DownloadAndInstall"] = "İndir ve Güncelle",
            ["Updates_Downloading"] = "Güncelleme indiriliyor...",
            ["Updates_InstallPrompt"] = "Güncelleme hazır. Şimdi kurulup Lyrow yeniden başlatılsın mı?",
            ["Updates_InstallBtn"] = "Kur ve Yeniden Başlat",
            ["Updates_UpToDate"] = "Lyrow güncel.",
            ["Updates_Available"] = "Yeni sürüm mevcut",
            ["Updates_Preparing"] = "Güncelleme hazırlanıyor...",
            ["Updates_Installing"] = "Güncelleme yükleniyor...",
            ["Updates_Complete"] = "Güncelleme tamamlandı",
            ["Updates_Failed"] = "Güncelleme başarısız",
            ["Updates_Retry"] = "Tekrar Dene",

            ["Shortcuts_Title"] = "Kısayollar",
            ["Shortcuts_Desc"] = "Kullanıcı tanımlı genel kısayol tuşları. Çakışmaları önlemek için zorunlu kısayol yoktur.",
            ["Shortcuts_ToggleGlow"] = "Glow Aç/Kapat:",
            ["Shortcuts_ToggleLyrics"] = "Sözleri Aç/Kapat:",
            ["Shortcuts_Clear"] = "Temizle",
            ["Shortcuts_Apply"] = "Kısayolları Kaydet",

            ["Diag_ResetSettings"] = "Ayarları Varsayılana Sıfırla",
            ["Diag_OpenLogs"] = "Tanılama Günlüğünü Aç"
        }
    };
}

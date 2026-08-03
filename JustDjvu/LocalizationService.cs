using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace JustDjvu;

public static class LocalizationService
{
    private static readonly ConditionalWeakTable<DependencyObject, OriginalValues> Originals = new();
    private static readonly ConditionalWeakTable<Window, object> AttachedWindows = new();

    // Порядок переводов: English, German, French, Spanish.
    private static readonly Dictionary<string, string[]> Translations = new(StringComparer.Ordinal)
    {
        ["_Файл"] = ["_File", "_Datei", "_Fichier", "_Archivo"],
        ["_Открыть…"] = ["_Open…", "Ö_ffnen…", "_Ouvrir…", "_Abrir…"],
        ["Недавние файлы"] = ["Recent files", "Zuletzt geöffnet", "Fichiers récents", "Archivos recientes"],
        ["Сохранить _копию…"] = ["Save a _copy…", "_Kopie speichern…", "Enregistrer une _copie…", "Guardar una _copia…"],
        ["_Экспорт страницы в PNG…"] = ["_Export page to PNG…", "Seite als PNG _exportieren…", "_Exporter la page en PNG…", "_Exportar página a PNG…"],
        ["_Печать страницы…"] = ["_Print page…", "Seite _drucken…", "_Imprimer la page…", "Im_primir página…"],
        ["_Закрыть документ"] = ["_Close document", "Dokument _schließen", "_Fermer le document", "_Cerrar documento"],
        ["Вы_ход"] = ["E_xit", "_Beenden", "_Quitter", "_Salir"],
        ["_Правка"] = ["_Edit", "_Bearbeiten", "É_dition", "_Editar"],
        ["_Копировать страницу"] = ["_Copy page", "Seite _kopieren", "_Copier la page", "_Copiar página"],
        ["_Найти…"] = ["_Find…", "_Suchen…", "_Rechercher…", "_Buscar…"],
        ["_Закладка на странице"] = ["_Bookmark page", "Seite als _Lesezeichen", "_Ajouter un signet", "_Marcar página"],
        ["_Вид"] = ["_View", "_Ansicht", "_Affichage", "_Ver"],
        ["_Боковая панель"] = ["_Sidebar", "_Seitenleiste", "Barre _latérale", "Barra _lateral"],
        ["Панель _инструментов"] = ["_Toolbar", "_Symbolleiste", "Barre d'_outils", "Barra de _herramientas"],
        ["_Одна страница"] = ["_Single page", "_Einzelseite", "Page _unique", "_Una página"],
        ["_Непрерывно"] = ["_Continuous", "_Fortlaufend", "_Continu", "_Continuo"],
        ["_Разворот"] = ["_Facing pages", "_Doppelseite", "Pages en _vis-à-vis", "Páginas _enfrentadas"],
        ["Вместить _страницу"] = ["Fit _page", "_Seite einpassen", "Ajuster à la _page", "Ajustar _página"],
        ["По _ширине"] = ["Fit _width", "An _Breite anpassen", "Ajuster à la _largeur", "Ajustar al _ancho"],
        ["_Реальный размер"] = ["_Actual size", "_Originalgröße", "Taille _réelle", "Tamaño _real"],
        ["_Увеличить"] = ["Zoom _in", "Ver_größern", "_Agrandir", "_Ampliar"],
        ["У_меньшить"] = ["Zoom _out", "Ver_kleinern", "_Réduire", "Re_ducir"],
        ["_Повернуть по часовой"] = ["_Rotate clockwise", "Im Uhrzeigersinn _drehen", "_Rotation horaire", "_Girar a la derecha"],
        ["_Тёмная тема"] = ["_Dark theme", "_Dunkles Design", "Thème _sombre", "Tema _oscuro"],
        ["Полный _экран"] = ["_Full screen", "_Vollbild", "Plein é_cran", "Pantalla _completa"],
        ["_Переход"] = ["_Navigate", "_Navigation", "_Navigation", "_Navegar"],
        ["_Первая страница"] = ["_First page", "_Erste Seite", "_Première page", "_Primera página"],
        ["_Предыдущая страница"] = ["_Previous page", "_Vorherige Seite", "Page _précédente", "Página _anterior"],
        ["_Следующая страница"] = ["_Next page", "_Nächste Seite", "Page _suivante", "Página _siguiente"],
        ["П_оследняя страница"] = ["_Last page", "_Letzte Seite", "_Dernière page", "Ú_ltima página"],
        ["_Инструменты"] = ["_Tools", "_Werkzeuge", "_Outils", "_Herramientas"],
        ["_Настройки…"] = ["_Settings…", "_Einstellungen…", "_Paramètres…", "_Ajustes…"],
        ["Зарегистрировать для ._djvu"] = ["Register for ._djvu", "Für ._djvu registrieren", "Associer aux fichiers ._djvu", "Registrar para ._djvu"],
        ["_Справка"] = ["_Help", "_Hilfe", "_Aide", "A_yuda"],
        ["_Горячие клавиши"] = ["_Keyboard shortcuts", "_Tastenkürzel", "_Raccourcis clavier", "_Atajos de teclado"],
        ["_О программе"] = ["_About", "Ü_ber", "À _propos", "_Acerca de"],
        ["Сохранить копию"] = ["Save a copy", "Kopie speichern", "Enregistrer une copie", "Guardar una copia"],
        ["Печать"] = ["Print", "Drucken", "Imprimer", "Imprimir"],
        ["Предыдущая страница"] = ["Previous page", "Vorherige Seite", "Page précédente", "Página anterior"],
        ["Следующая страница"] = ["Next page", "Nächste Seite", "Page suivante", "Página siguiente"],
        ["Уменьшить"] = ["Zoom out", "Verkleinern", "Réduire", "Reducir"],
        ["Масштаб"] = ["Zoom", "Zoom", "Zoom", "Zoom"],
        ["Увеличить"] = ["Zoom in", "Vergrößern", "Agrandir", "Ampliar"],
        ["Вместить страницу"] = ["Fit page", "Seite einpassen", "Ajuster à la page", "Ajustar página"],
        ["По ширине"] = ["Fit width", "An Breite anpassen", "Ajuster à la largeur", "Ajustar al ancho"],
        ["Одна страница"] = ["Single page", "Einzelseite", "Page unique", "Una página"],
        ["Непрерывно"] = ["Continuous", "Fortlaufend", "Continu", "Continuo"],
        ["Разворот"] = ["Facing pages", "Doppelseite", "Pages en vis-à-vis", "Páginas enfrentadas"],
        ["Повернуть"] = ["Rotate", "Drehen", "Rotation", "Girar"],
        ["Полный экран"] = ["Full screen", "Vollbild", "Plein écran", "Pantalla completa"],
        ["Страницы"] = ["Pages", "Seiten", "Pages", "Páginas"],
        ["Загрузка…"] = ["Loading…", "Laden…", "Chargement…", "Cargando…"],
        ["Закладки"] = ["Bookmarks", "Lesezeichen", "Signets", "Marcadores"],
        ["+ Закладка на текущей странице"] = ["+ Bookmark current page", "+ Aktuelle Seite merken", "+ Ajouter un signet", "+ Marcar página actual"],
        ["Страница "] = ["Page ", "Seite ", "Page ", "Página "],
        ["Поиск"] = ["Search", "Suche", "Recherche", "Búsqueda"],
        ["Найти"] = ["Find", "Suchen", "Rechercher", "Buscar"],
        ["Поиск по распознанному тексту"] = ["Search recognized text", "Erkannten Text durchsuchen", "Rechercher dans le texte reconnu", "Buscar en texto reconocido"],
        ["Отмена"] = ["Cancel", "Abbrechen", "Annuler", "Cancelar"],
        ["Введите текст для поиска"] = ["Enter text to search", "Suchtext eingeben", "Saisissez le texte à rechercher", "Introduzca texto para buscar"],
        ["Откройте документ DjVu"] = ["Open a DjVu document", "DjVu-Dokument öffnen", "Ouvrez un document DjVu", "Abra un documento DjVu"],
        ["Перетащите файл сюда или воспользуйтесь кнопкой"] = ["Drop a file here or use the button", "Datei hier ablegen oder Schaltfläche verwenden", "Déposez un fichier ici ou utilisez le bouton", "Arrastre un archivo aquí o use el botón"],
        ["Открыть файл…"] = ["Open file…", "Datei öffnen…", "Ouvrir un fichier…", "Abrir archivo…"],
        ["Открытие документа…"] = ["Opening document…", "Dokument wird geöffnet…", "Ouverture du document…", "Abriendo documento…"],
        ["Отпустите файл, чтобы открыть"] = ["Drop the file to open it", "Datei zum Öffnen ablegen", "Déposez le fichier pour l’ouvrir", "Suelte el archivo para abrirlo"],
        ["Готово"] = ["Ready", "Bereit", "Prêt", "Listo"],
        ["Документ не открыт"] = ["No document open", "Kein Dokument geöffnet", "Aucun document ouvert", "No hay documento abierto"],
        ["Настройки JustDjVu"] = ["JustDjVu Settings", "JustDjVu-Einstellungen", "Paramètres de JustDjVu", "Ajustes de JustDjVu"],
        ["Общие"] = ["General", "Allgemein", "Général", "General"],
        ["Язык интерфейса"] = ["Interface language", "Sprache der Oberfläche", "Langue de l’interface", "Idioma de la interfaz"],
        ["Тема интерфейса"] = ["Interface theme", "Oberflächendesign", "Thème de l’interface", "Tema de la interfaz"],
        ["Светлая"] = ["Light", "Hell", "Clair", "Claro"],
        ["Тёмная"] = ["Dark", "Dunkel", "Sombre", "Oscuro"],
        ["Режим просмотра по умолчанию"] = ["Default view mode", "Standard-Ansichtsmodus", "Mode d’affichage par défaut", "Modo de vista predeterminado"],
        ["Размер страницы по умолчанию"] = ["Default page size", "Standard-Seitengröße", "Taille de page par défaut", "Tamaño de página predeterminado"],
        ["Реальный размер (100%)"] = ["Actual size (100%)", "Originalgröße (100 %)", "Taille réelle (100 %)", "Tamaño real (100 %)"],
        ["Свой масштаб"] = ["Custom zoom", "Benutzerdefinierter Zoom", "Zoom personnalisé", "Zoom personalizado"],
        ["Элементы интерфейса"] = ["Interface elements", "Oberflächenelemente", "Éléments de l’interface", "Elementos de la interfaz"],
        ["Открывать последний документ при запуске"] = ["Open last document at startup", "Letztes Dokument beim Start öffnen", "Ouvrir le dernier document au démarrage", "Abrir el último documento al iniciar"],
        ["Показывать боковую панель"] = ["Show sidebar", "Seitenleiste anzeigen", "Afficher la barre latérale", "Mostrar barra lateral"],
        ["Показывать панель инструментов"] = ["Show toolbar", "Symbolleiste anzeigen", "Afficher la barre d’outils", "Mostrar barra de herramientas"],
        ["Щёлкните сочетание и нажмите клавиши или прокрутите колесо мыши."] = ["Click a shortcut, then press keys or scroll the mouse wheel.", "Kürzel anklicken, dann Tasten drücken oder Mausrad drehen.", "Cliquez sur un raccourci, puis appuyez sur les touches ou tournez la molette.", "Haga clic en un atajo y pulse las teclas o gire la rueda."],
        ["Действие"] = ["Action", "Aktion", "Action", "Acción"],
        ["Основное"] = ["Primary", "Primär", "Principal", "Principal"],
        ["Дополнительное"] = ["Secondary", "Sekundär", "Secondaire", "Secundario"],
        ["Сбросить всё"] = ["Reset all", "Alles zurücksetzen", "Tout réinitialiser", "Restablecer todo"],
        ["Esc — отменить ввод"] = ["Esc — cancel input", "Esc — Eingabe abbrechen", "Échap — annuler la saisie", "Esc — cancelar entrada"],
        ["Сохранить"] = ["Save", "Speichern", "Enregistrer", "Guardar"],
        ["Открыть документ"] = ["Open document", "Dokument öffnen", "Ouvrir le document", "Abrir documento"],
        ["Закрыть документ"] = ["Close document", "Dokument schließen", "Fermer le document", "Cerrar documento"],
        ["Печать текущей страницы"] = ["Print current page", "Aktuelle Seite drucken", "Imprimer la page actuelle", "Imprimir página actual"],
        ["Поиск по тексту"] = ["Search text", "Text suchen", "Rechercher du texte", "Buscar texto"],
        ["Первая страница"] = ["First page", "Erste Seite", "Première page", "Primera página"],
        ["Последняя страница"] = ["Last page", "Letzte Seite", "Dernière page", "Última página"],
        ["Масштаб 100%"] = ["Zoom 100%", "Zoom 100 %", "Zoom 100 %", "Zoom 100 %"],
        ["Повернуть страницу"] = ["Rotate page", "Seite drehen", "Faire pivoter la page", "Girar página"],
        ["Показать/скрыть боковую панель"] = ["Show/hide sidebar", "Seitenleiste ein-/ausblenden", "Afficher/masquer la barre latérale", "Mostrar/ocultar barra lateral"],
        ["Полноэкранный режим"] = ["Full-screen mode", "Vollbildmodus", "Mode plein écran", "Modo de pantalla completa"],
        ["Добавить/удалить закладку"] = ["Add/remove bookmark", "Lesezeichen hinzufügen/entfernen", "Ajouter/supprimer le signet", "Añadir/eliminar marcador"],
        ["Колесо вверх"] = ["Wheel up", "Mausrad nach oben", "Molette vers le haut", "Rueda hacia arriba"],
        ["Колесо вниз"] = ["Wheel down", "Mausrad nach unten", "Molette vers le bas", "Rueda hacia abajo"],
        ["Список пуст"] = ["The list is empty", "Liste ist leer", "La liste est vide", "La lista está vacía"],
        ["Очистить список"] = ["Clear list", "Liste leeren", "Effacer la liste", "Borrar lista"],
        ["Документ открыт"] = ["Document opened", "Dokument geöffnet", "Document ouvert", "Documento abierto"],
        ["Документ закрыт"] = ["Document closed", "Dokument geschlossen", "Document fermé", "Documento cerrado"],
        ["Операция отменена"] = ["Operation cancelled", "Vorgang abgebrochen", "Opération annulée", "Operación cancelada"],
        ["Копия сохранена"] = ["Copy saved", "Kopie gespeichert", "Copie enregistrée", "Copia guardada"],
        ["Страница экспортирована"] = ["Page exported", "Seite exportiert", "Page exportée", "Página exportada"],
        ["Страница скопирована"] = ["Page copied", "Seite kopiert", "Page copiée", "Página copiada"],
        ["Страница отправлена на печать"] = ["Page sent to printer", "Seite an Drucker gesendet", "Page envoyée à l’imprimante", "Página enviada a la impresora"],
        ["Закладка удалена"] = ["Bookmark removed", "Lesezeichen entfernt", "Signet supprimé", "Marcador eliminado"],
        ["Закладка добавлена"] = ["Bookmark added", "Lesezeichen hinzugefügt", "Signet ajouté", "Marcador añadido"],
        ["Сначала откройте документ"] = ["Open a document first", "Zuerst ein Dokument öffnen", "Ouvrez d’abord un document", "Abra primero un documento"],
        ["Поиск…"] = ["Searching…", "Suche…", "Recherche…", "Buscando…"],
        ["Совпадений не найдено"] = ["No matches found", "Keine Treffer", "Aucun résultat", "No se encontraron coincidencias"],
        ["Поиск отменён"] = ["Search cancelled", "Suche abgebrochen", "Recherche annulée", "Búsqueda cancelada"],
        ["Ошибка поиска"] = ["Search error", "Suchfehler", "Erreur de recherche", "Error de búsqueda"],
        ["Горячие клавиши"] = ["Keyboard shortcuts", "Tastenkürzel", "Raccourcis clavier", "Atajos de teclado"],
        ["О программе"] = ["About", "Über", "À propos", "Acerca de"],
        ["Повторяющееся сочетание"] = ["Duplicate shortcut", "Doppeltes Kürzel", "Raccourci en double", "Atajo duplicado"],
        ["Нажмите сочетание клавиш; Backspace — очистить, Esc — отменить"] = ["Press a shortcut; Backspace — clear, Esc — cancel", "Kürzel drücken; Rücktaste — löschen, Esc — abbrechen", "Appuyez sur un raccourci ; Retour arrière — effacer, Échap — annuler", "Pulse un atajo; Retroceso — borrar, Esc — cancelar"],
        ["Это сочетание нельзя назначить"] = ["This shortcut cannot be assigned", "Dieses Kürzel kann nicht zugewiesen werden", "Ce raccourci ne peut pas être attribué", "Este atajo no se puede asignar"],
        ["Назначено: колесо вверх"] = ["Assigned: wheel up", "Zugewiesen: Mausrad nach oben", "Attribué : molette vers le haut", "Asignado: rueda hacia arriba"],
        ["Назначено: колесо вниз"] = ["Assigned: wheel down", "Zugewiesen: Mausrad nach unten", "Attribué : molette vers le bas", "Asignado: rueda hacia abajo"],
        ["Не удалось открыть документ"] = ["Could not open document", "Dokument konnte nicht geöffnet werden", "Impossible d’ouvrir le document", "No se pudo abrir el documento"],
        ["Отрисовка страницы {0}…"] = ["Rendering page {0}…", "Seite {0} wird gerendert…", "Rendu de la page {0}…", "Renderizando página {0}…"],
        ["Не удалось отобразить страницу {0}"] = ["Could not display page {0}", "Seite {0} konnte nicht angezeigt werden", "Impossible d’afficher la page {0}", "No se pudo mostrar la página {0}"],
        ["{0}  •  {1} стр."] = ["{0}  •  {1} pages", "{0}  •  {1} Seiten", "{0}  •  {1} pages", "{0}  •  {1} páginas"],
        ["Не удалось сохранить настройки"] = ["Could not save settings", "Einstellungen konnten nicht gespeichert werden", "Impossible d’enregistrer les paramètres", "No se pudieron guardar los ajustes"],
        ["Открыть документ DjVu"] = ["Open DjVu document", "DjVu-Dokument öffnen", "Ouvrir un document DjVu", "Abrir documento DjVu"],
        ["Документы DjVu (*.djvu;*.djv)|*.djvu;*.djv|Все файлы (*.*)|*.*"] = ["DjVu documents (*.djvu;*.djv)|*.djvu;*.djv|All files (*.*)|*.*", "DjVu-Dokumente (*.djvu;*.djv)|*.djvu;*.djv|Alle Dateien (*.*)|*.*", "Documents DjVu (*.djvu;*.djv)|*.djvu;*.djv|Tous les fichiers (*.*)|*.*", "Documentos DjVu (*.djvu;*.djv)|*.djvu;*.djv|Todos los archivos (*.*)|*.*"],
        ["Документ DjVu (*.djvu)|*.djvu|Все файлы (*.*)|*.*"] = ["DjVu document (*.djvu)|*.djvu|All files (*.*)|*.*", "DjVu-Dokument (*.djvu)|*.djvu|Alle Dateien (*.*)|*.*", "Document DjVu (*.djvu)|*.djvu|Tous les fichiers (*.*)|*.*", "Documento DjVu (*.djvu)|*.djvu|Todos los archivos (*.*)|*.*"],
        ["Не удалось сохранить копию"] = ["Could not save copy", "Kopie konnte nicht gespeichert werden", "Impossible d’enregistrer la copie", "No se pudo guardar la copia"],
        ["Экспорт страницы"] = ["Export page", "Seite exportieren", "Exporter la page", "Exportar página"],
        ["Изображение PNG (*.png)|*.png"] = ["PNG image (*.png)|*.png", "PNG-Bild (*.png)|*.png", "Image PNG (*.png)|*.png", "Imagen PNG (*.png)|*.png"],
        ["Не удалось экспортировать страницу"] = ["Could not export page", "Seite konnte nicht exportiert werden", "Impossible d’exporter la page", "No se pudo exportar la página"],
        ["Не удалось скопировать страницу"] = ["Could not copy page", "Seite konnte nicht kopiert werden", "Impossible de copier la page", "No se pudo copiar la página"],
        ["Ошибка печати"] = ["Print error", "Druckfehler", "Erreur d’impression", "Error de impresión"],
        ["Поиск… {0}%"] = ["Searching… {0}%", "Suche… {0} %", "Recherche… {0} %", "Buscando… {0}%"],
        ["Найдено: {0}"] = ["Found: {0}", "Gefunden: {0}", "Résultats : {0}", "Encontrados: {0}"],
        ["Не удалось выполнить поиск"] = ["Could not search document", "Dokument konnte nicht durchsucht werden", "Impossible d’effectuer la recherche", "No se pudo realizar la búsqueda"],
        ["JustDjVu зарегистрирован для файлов .djvu и .djv текущего пользователя.\n\nТеперь приложение появится в меню «Открыть с помощью»."] = ["JustDjVu is registered for .djvu and .djv files for the current user.\n\nThe app will now appear in the “Open with” menu.", "JustDjVu wurde für .djvu- und .djv-Dateien des aktuellen Benutzers registriert.\n\nDie App erscheint nun im Menü „Öffnen mit“.", "JustDjVu est associé aux fichiers .djvu et .djv pour l’utilisateur actuel.\n\nL’application apparaîtra désormais dans le menu « Ouvrir avec ».", "JustDjVu está registrado para archivos .djvu y .djv del usuario actual.\n\nLa aplicación aparecerá en el menú «Abrir con»."],
        ["Регистрация завершена"] = ["Registration complete", "Registrierung abgeschlossen", "Association terminée", "Registro completado"],
        ["Не удалось зарегистрировать приложение"] = ["Could not register application", "Anwendung konnte nicht registriert werden", "Impossible d’associer l’application", "No se pudo registrar la aplicación"],
        ["JustDjVu 1.0\n\nСовременный DjVu-ридер для Windows.\nРендеринг документов: DjVuLibre 3.5.29 (GPL v2+).\n\nПоддерживает масштабирование, режимы просмотра, поиск OCR-текста, закладки, печать, drag & drop и «Открыть с помощью»."] = ["JustDjVu 1.0\n\nA modern DjVu reader for Windows.\nDocument rendering: DjVuLibre 3.5.29 (GPL v2+).\n\nSupports zoom, view modes, OCR text search, bookmarks, printing, drag & drop and “Open with”.", "JustDjVu 1.0\n\nEin moderner DjVu-Reader für Windows.\nDokumentdarstellung: DjVuLibre 3.5.29 (GPL v2+).\n\nUnterstützt Zoom, Ansichtsmodi, OCR-Textsuche, Lesezeichen, Drucken, Drag & Drop und „Öffnen mit“.", "JustDjVu 1.0\n\nUn lecteur DjVu moderne pour Windows.\nRendu des documents : DjVuLibre 3.5.29 (GPL v2+).\n\nPrend en charge le zoom, les modes d’affichage, la recherche OCR, les signets, l’impression, le glisser-déposer et « Ouvrir avec ».", "JustDjVu 1.0\n\nUn lector DjVu moderno para Windows.\nRenderizado: DjVuLibre 3.5.29 (GPL v2+).\n\nAdmite zoom, modos de vista, búsqueda OCR, marcadores, impresión, arrastrar y soltar y «Abrir con»."],
        ["Сочетание «{0}» назначено нескольким действиям."] = ["Shortcut “{0}” is assigned to multiple actions.", "Das Kürzel „{0}“ ist mehreren Aktionen zugewiesen.", "Le raccourci « {0} » est attribué à plusieurs actions.", "El atajo «{0}» está asignado a varias acciones."],
        ["Не удалось определить путь приложения."] = ["Could not determine application path.", "Anwendungspfad konnte nicht ermittelt werden.", "Impossible de déterminer le chemin de l’application.", "No se pudo determinar la ruta de la aplicación."],
        ["Документ DjVu"] = ["DjVu document", "DjVu-Dokument", "Document DjVu", "Documento DjVu"],
        ["Файл не найден."] = ["File not found.", "Datei nicht gefunden.", "Fichier introuvable.", "Archivo no encontrado."],
        ["Поддерживаются файлы .djvu и .djv."] = ["Only .djvu and .djv files are supported.", "Es werden .djvu- und .djv-Dateien unterstützt.", "Les fichiers .djvu et .djv sont pris en charge.", "Se admiten archivos .djvu y .djv."],
        ["Не удалось определить количество страниц DjVu."] = ["Could not determine DjVu page count.", "Die Anzahl der DjVu-Seiten konnte nicht ermittelt werden.", "Impossible de déterminer le nombre de pages DjVu.", "No se pudo determinar el número de páginas DjVu."],
        ["Не удалось отобразить страницу {0}."] = ["Could not render page {0}.", "Seite {0} konnte nicht gerendert werden.", "Impossible de rendre la page {0}.", "No se pudo renderizar la página {0}."],
        ["Компонент DjVuLibre «{0}» не найден рядом с приложением."] = ["DjVuLibre component “{0}” was not found next to the application.", "Die DjVuLibre-Komponente „{0}“ wurde nicht neben der Anwendung gefunden.", "Le composant DjVuLibre « {0} » est introuvable à côté de l’application.", "El componente DjVuLibre «{0}» no se encontró junto a la aplicación."],
        ["Документ не открыт."] = ["No document is open.", "Kein Dokument ist geöffnet.", "Aucun document n’est ouvert.", "No hay ningún documento abierto."],
        ["Не удалось запустить {0}."] = ["Could not start {0}.", "{0} konnte nicht gestartet werden.", "Impossible de démarrer {0}.", "No se pudo iniciar {0}."],
        ["Страница {0}"] = ["Page {0}", "Seite {0}", "Page {0}", "Página {0}"],
        ["Настройки сохраняются отдельно для текущего пользователя. Размер окна и положение полос прокрутки Windows восстанавливает автоматически."] = ["Settings are stored separately for the current user. Windows restores the window size and scroll positions automatically.", "Einstellungen werden für den aktuellen Benutzer gespeichert. Windows stellt Fenstergröße und Bildlaufpositionen automatisch wieder her.", "Les paramètres sont enregistrés pour l’utilisateur actuel. Windows restaure automatiquement la taille de la fenêtre et les positions de défilement.", "Los ajustes se guardan para el usuario actual. Windows restaura automáticamente el tamaño de la ventana y las posiciones de desplazamiento."]
    };

    public static AppLanguage CurrentLanguage { get; private set; } = AppLanguage.Russian;

    public static void SetLanguage(AppLanguage language)
    {
        CurrentLanguage = language;
        if (Application.Current is null)
        {
            return;
        }

        foreach (Window window in Application.Current.Windows)
        {
            Attach(window);
            ApplyTree(window);
        }
    }

    public static string Translate(string source)
    {
        if (CurrentLanguage == AppLanguage.Russian ||
            !Translations.TryGetValue(source, out var values))
        {
            return source;
        }

        var index = (int)CurrentLanguage - 1;
        return index >= 0 && index < values.Length ? values[index] : source;
    }

    public static string Format(string source, params object[] arguments) =>
        string.Format(Translate(source), arguments);

    public static void Attach(Window window)
    {
        if (!AttachedWindows.TryGetValue(window, out _))
        {
            AttachedWindows.Add(window, new object());
            window.AddHandler(
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(Element_Loaded),
                true);
        }
        ApplyTree(window);
    }

    private static void Element_Loaded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source)
        {
            ApplyTree(source);
        }
    }

    private static void ApplyTree(DependencyObject root)
    {
        var visited = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<DependencyObject>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!visited.Add(current))
            {
                continue;
            }

            ApplyElement(current);

            foreach (var child in LogicalTreeHelper.GetChildren(current).OfType<DependencyObject>())
            {
                pending.Push(child);
            }

            if (current is ItemsControl itemsControl)
            {
                foreach (var item in itemsControl.Items.OfType<DependencyObject>())
                {
                    pending.Push(item);
                }
            }

            try
            {
                for (var index = 0; index < VisualTreeHelper.GetChildrenCount(current); index++)
                {
                    pending.Push(VisualTreeHelper.GetChild(current, index));
                }
            }
            catch (InvalidOperationException)
            {
                // Некоторые невизуальные WPF-объекты не имеют визуального дерева.
            }
        }
    }

    private static void ApplyElement(DependencyObject element)
    {
        if (element is HeaderedContentControl)
        {
            ApplyProperty(element, HeaderedContentControl.HeaderProperty);
        }
        if (element is HeaderedItemsControl)
        {
            ApplyProperty(element, HeaderedItemsControl.HeaderProperty);
        }
        if (element is ContentControl)
        {
            ApplyProperty(element, ContentControl.ContentProperty);
        }
        if (element is TextBlock)
        {
            ApplyProperty(element, TextBlock.TextProperty);
        }
        if (element is FrameworkElement)
        {
            ApplyProperty(element, FrameworkElement.ToolTipProperty);
        }
        if (element is DataGrid grid)
        {
            foreach (var column in grid.Columns)
            {
                ApplyProperty(column, DataGridColumn.HeaderProperty);
            }
        }
    }

    private static void ApplyProperty(DependencyObject element, DependencyProperty property)
    {
        if (BindingOperations.IsDataBound(element, property) ||
            element.GetValue(property) is not string current ||
            string.IsNullOrWhiteSpace(current))
        {
            return;
        }

        var originalValues = Originals.GetOrCreateValue(element);
        if (!originalValues.Values.TryGetValue(property, out var original))
        {
            original = current;
            originalValues.Values[property] = original;
        }
        element.SetCurrentValue(property, Translate(original));
    }

    private sealed class OriginalValues
    {
        public Dictionary<DependencyProperty, string> Values { get; } = [];
    }
}

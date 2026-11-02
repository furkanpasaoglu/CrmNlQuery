(function () {
  'use strict';
  if (window.top !== window.self) { return; }
  if (window.__nlQueryWidget) { return; }

  var scriptSrc = document.currentScript && document.currentScript.src;
  if (!scriptSrc) { return; }
  var parts = scriptSrc.match(/^(.*)\/widget\/widget\.js(?:\?.*)?$/);
  if (!parts) { return; }
  var base = parts[1];
  if (location.href.indexOf(base + '/') === 0) { return; }
  window.__nlQueryWidget = true;

  var T_EN = {
    title: 'Ask your CRM',
    openAdmin: 'Open the admin panel',
    signInFailed: 'Could not sign you in through the CRM. Reload the page, or tell your administrator if it keeps happening.',
    chartView: 'Chart',
    tableView: 'Table',
    chartPng: 'Download PNG',
    chartPartial: 'The chart shows partial data: the result was cut off.',
    presentTable: 'Showing the table.',
    presentChart: 'Showing the chart.',
    presentLineNote: 'A line chart is used for results by date; this result is by category, so it is shown as a bar chart.',
    presentBarNote: 'This result is by date, so it is shown as a line chart.',
    presentNothing: 'There is no answer to show yet. Ask a question first; then I can show the result as a chart or a table.',
    chartNoneFew: 'No chart: a chart needs at least two categories, and this result has fewer.',
    chartNoneList: 'No chart: this is a list of records. Ask for a count or total grouped by something, for example by status or city.',
    chartNoneUnsupported: 'No chart: this result does not fit a chart (a chart shows one measure grouped by one category or by date).',
    chartNoneChain: 'No chart: charts are not shown for questions answered in several steps.',
    chartOmitted: '{n} more categories are only in the table.',
    chartOther: 'Other',
    chartEmpty: '(empty)',
    chartLine: 'Line chart',
    chartBar: 'Bar chart',
    newChat: 'New chat',
    newChatTitle: 'New chat',
    searchChats: 'Search chats',
    noChats: 'No chats yet',
    noMatch: 'No matching chats',
    groupPinned: 'Pinned',
    groupToday: 'Today',
    groupYesterday: 'Yesterday',
    groupWeek: 'Previous 7 days',
    groupOlder: 'Older',
    menuRename: 'Rename',
    menuPin: 'Pin',
    menuUnpin: 'Unpin',
    menuDelete: 'Delete',
    options: 'Options',
    toggleSidebar: 'Show or hide the chat list',
    minimize: 'Minimize',
    maximize: 'Full screen',
    restore: 'Restore size',
    close: 'Close',
    resize: 'Drag to resize',
    setBack: '← Back to chat',
    setSecAppearance: 'Appearance',
    setSecChat: 'Chat',
    setSecProfile: 'Profile and language',
    setSecPrivacy: 'Privacy and data',
    setSecHelp: 'Help and shortcuts',
    setTheme: 'Theme',
    setTextSize: 'Text size',
    sizeSmall: 'Small',
    sizeNormal: 'Normal',
    sizeLarge: 'Large',
    setDensity: 'Table density',
    densityComfortable: 'Comfortable',
    densityCompact: 'Compact',
    setResetWindow: 'Reset window size',
    setResetDone: 'Window size reset.',
    setPreviewRows: 'Rows shown in a chat table',
    setExplainOpen: 'Always show how the question was understood',
    setSuggestions: 'Show suggested next steps',
    setChatNote: 'These preferences are kept in this browser.',
    setNameSaved: 'Name saved.',
    setDownload: 'Download this chat',
    setDownloadNone: 'Open a chat first.',
    setDlYou: 'You',
    setDlBot: 'Assistant',
    setDataTitle: 'Your data',
    setPrivacyNote: 'Result rows are never stored; only your questions and the queries behind the answers are.',
    setShortcuts: 'Keyboard shortcuts',
    shortcutSend: 'Enter: send the question',
    shortcutCmd: '/: open the command menu',
    shortcutTab: 'Tab: complete the highlighted command',
    shortcutEsc: 'Esc: close a menu or dialog',
    setCommands: 'Commands',
    themeLight: 'Light',
    themeDark: 'Dark',
    themeAuto: 'Auto',
    loading: 'Loading…',
    loadingEarlier: 'Loading earlier messages…',
    startOfChat: 'Start of chat',
    retry: 'Retry',
    loadFailed: 'Could not load.',
    chatGone: 'This chat is no longer available.',
    show: 'Show',
    running: 'Running…',
    greeting: 'What would you like to know?',
    greetingNamed: 'Hello {name}, what would you like to know?',
    nameTitle: 'How should we address you?',
    nameHint: 'Shown in the greeting. Only you see it. Leave it empty to remove it.',
    namePlaceholder: 'Your name',
    nameFailed: 'Could not save your name.',
    save: 'Save',
    greetingHint: 'Ask in plain words. Follow-ups such as "of these, only those over 50k" continue in the same chat.',
    examples: ['Show accounts in London', 'Show open opportunities of this customer', 'Show all products'],
    placeholder: 'Ask a question\u2026',
    placeholderExample: 'e.g. {text}',
    ask: 'Ask',
    thinking: 'Thinking…',
    unreachable: 'Could not reach the service ({msg}).',
    chip: 'Open record: {entity} (questions like "this customer" refer to it)',
    chipOff: 'Ignore the open record',
    about: 'About: {name}',
    glossaryLine: '{term} (glossary): {text}',
    retentionDays: 'Chats are deleted after {n} days (pinned chats are kept).',
    retentionNone: 'Chats are not deleted automatically.',
    auditNotice: 'Your questions may be viewed by administrators for audit purposes.',
    settings: 'Settings',
    deleteAllMenu: 'Delete all chat history…',
    renameFailed: 'Could not rename the chat.',
    pinFailed: 'Could not change the pin.',
    cancel: 'Cancel',
    del: 'Delete',
    deleteAll: 'Delete all',
    deleting: 'Deleting…',
    deleteFailed: 'Could not delete. Try again.',
    deleteChatTitle: 'Delete this chat?',
    deleteChatBody: '"{name}" and its questions will be permanently deleted. This cannot be undone.',
    deleteChatPinned: 'This chat is pinned; deleting it removes the pin too.',
    deleteAllTitle: 'Delete all chat history?',
    deleteAllBody: '{n} chats will be permanently deleted. Your {p} pinned chats are kept. This cannot be undone.',
    deleteAllBodyNoPinned: '{n} chats will be permanently deleted. This cannot be undone.',
    deleteAllBodyWithPinned: '{n} chats, including {p} pinned, will be permanently deleted. This cannot be undone.',
    deleteAllIncludePinned: 'Also delete my {p} pinned chats',
    deleteAllNothing: 'There is nothing to delete.',
    crmNote: 'Your CRM data is not affected; only your chat history is removed.',
    fetchXml: 'Generated FetchXML',
    copy: 'Copy',
    copied: 'Copied',
    truncated: 'Result truncated at 500 rows — narrow your question.',
    countOf: 'Showing the first {shown} of {total} {entity}records \u2014 narrow your question.',
    countMore: 'Showing the first {shown} of more than {total} {entity}records \u2014 narrow your question.',
    countAll: 'All {total} {entity}records are shown.',
    suggestionsTitle: 'Next:',
    noRecords: 'No records found.',
    stepLabel: 'Step {n}: {question}',
    stepNoRows: 'no records found',
    stepMore: '… and {n} more',
    sumTitle: 'Summary of {name}',
    sumRefresh: 'Refresh',
    sumVia: '{title} (via {via})',
    sumOpen: 'Open',
    sumOpenRecord: 'Open record',
    sumEmpty: 'No records.',
    sumUnavailable: 'This section could not be loaded.',
    sumBasic: 'Details',
    sumOpportunities: 'Opportunities',
    sumActivities: 'Recent activities',
    sumCases: 'Cases',
    sumContracts: 'Contracts',
    sumOrders: 'Orders',
    sumFooter: 'Counts and lists include only what you are allowed to see in the CRM.',
    draftTitle: 'Email draft for {name}',
    draftWarning: 'Draft: check it before sending. Nothing has been sent.',
    createTitle: 'Create {entity}',
    createDoneTitle: '{entity}: result',
    createCancelled: 'Cancelled. Nothing was created.',
    createAskMissing: 'To create this I still need: {fields}. Reply in the chat with the missing information.',
    createAskFix: 'Some information needs fixing. Reply in the chat with the corrected values.',
    createRowsSummary: '{ready} of {total} records are ready.',
    createButton: 'Create',
    createButtonN: 'Create {n} records',
    createAnyway: 'Create anyway',
    createCancel: 'Cancel',
    createOpen: 'Open',
    createWorking: 'Working…',
    createDuplicate: 'A record with this name already exists: {names}.',
    createStatus_ready: 'Ready',
    createStatus_needs_info: 'Needs information',
    createStatus_created: 'Created',
    createStatus_failed: 'Failed',
    createStatus_skipped: 'Not created',
    createErr_permission: 'You do not have permission to create this record.',
    createErr_crm: 'The CRM could not create this record.',
    createProb_missing_required: '{label} is required.',
    createProb_unknown_field: 'I do not know the field “{label}”.',
    createProb_invalid_number: '{label} must be a number.',
    createProb_invalid_date: '{label} must be a date, for example 2026-10-26.',
    createProb_invalid_boolean: '{label} must be yes or no.',
    createProb_too_long: '{label} is too long (at most {detail} characters).',
    createProb_choice_not_found: '{label}: pick one of: {detail}.',
    createProb_lookup_not_found: '{label}: no record named “{detail}” was found.',
    createProb_lookup_ambiguous: '{label}: more than one record matches ({detail}). Be more specific.',
    draftTo: 'To:',
    draftSubject: 'Subject:',
    draftNoRecipient: '(no email address on record)',
    draftMailto: 'Open in email app',
    draftMailtoTooLong: 'This draft is too long for the email app link. Use Copy instead.',
    draftAgain: 'Draft again',
    draftUnresolved: 'Highlighted items could not be filled in from the CRM. Fill them in yourself.',
    draftCopyFailed: 'Could not copy automatically. The text is selected: press Ctrl+C.',
    fbUp: 'Good answer',
    fbDown: 'Not helpful',
    fbReasonTitle: 'What went wrong? (optional)',
    fbReasonWrongRecords: 'Wrong or missing records',
    fbReasonWrongColumns: 'Wrong columns',
    fbReasonMisunderstood: 'It did not understand my question',
    fbReasonTooSlow: 'Too slow',
    fbReasonOther: 'Something else',
    fbShare: 'Share this question with your administrator to help improve the tool.',
    fbShareWarn: 'If your question contains personal information, do not share it. Record names in the explanation are shared too.',
    fbSend: 'Send',
    fbThanks: 'Thanks for your feedback.',
    fbShared: 'Shared with your administrator.',
    fbWithdraw: 'Withdraw',
    fbWithdrawn: 'Sharing withdrawn.',
    fbFailed: 'Could not save your feedback. Try again.',
    qsAdd: 'Add to acceptance set',
    qsTitle: 'Save this answer as a correct example. {n} rows now; the rows themselves are not stored.',
    qsOrderTitle: 'How should rows be compared?',
    qsOrderAuto: 'Same order only if the question sorts',
    qsOrderOrdered: 'Same order always',
    qsOrderUnordered: 'Order does not matter',
    qsColsTitle: 'Which columns count?',
    qsColsRef: 'These columns; extra columns are fine',
    qsColsExact: 'Exactly these columns',
    qsConfirm: 'I checked this answer and it is correct',
    qsSave: 'Save',
    qsCancel: 'Cancel',
    qsSaved: 'Saved to the acceptance set.',
    qsDuplicate: 'This answer is already in the acceptance set.',
    sgAction: 'Suggest as a good example',
    sgNotice: 'Your question and the query that answered it will be shown to administrators as a test example. They are used only for testing: no name is attached and result rows are never shared. Nothing runs until an administrator approves it.',
    sgSend: 'Send suggestion',
    sgDone: 'Thanks. An administrator will review your suggestion.',
    sgDuplicate: 'A similar example already exists.',
    sgFailed: 'Could not send the suggestion.',
    rpFound: 'Reports that match your request',
    rpAll: 'Reports you can open (type a report name to find a specific one)',
    rpOpen: 'Open report',
    rpFor: 'For: {name}',
    rpHint: 'The report opens in the CRM in a new tab; you choose its filters there.',
    qsFailed: 'Could not save this answer. Try again.',
    fbDeleteNote: 'Questions you shared with your administrator from this chat are withdrawn too.',
    openAll: 'Open all {n} rows in a new tab',
    openRecord: 'Open',
    openRecordHint: 'Open this record in the CRM',
    openInCrm: 'Open in CRM',
    openInCrmHint: 'Opens every row in the CRM\'s own list view (paging, sorting, export)',
    openingInCrm: 'Opening\u2026',
    openInCrmFailed: 'Could not open it in the CRM. Use the "Open all rows" link instead.',
    openInCrmDenied: 'You do not have permission to create views in the CRM. Use the "Open all rows" link instead.',
    searchResults: 'Search in results…',
    rowsOf: ' {a} of {b} rows',
    prev: 'Prev',
    next: 'Next',
    pageOf: 'Page {a} / {b}',
    columnsHint: 'or type column names, e.g. id, name',
    rephraseHint: 'or rephrase your question',
    send: 'Send',
    stop: 'Stop',
    stopped: 'Stopped',
    cmdHelp: 'Show what you can ask and the available commands',
    cmdNew: 'Start a new chat',
    cmdEdit: 'Put your last question back in the box to change it',
    cmdHistory: 'Open the chat list and search it',
    cmdClear: 'Delete your chat history',
    noSuchCommand: 'No such command. Type /help.',
    helpTitle: 'What you can do here',
    helpCommands: 'Commands',
    helpExamples: 'Example questions (click to use)',
    editRemove: 'Remove this filter',
    editChange: 'Change this filter',
    editApply: 'Apply',
    editCancel: 'Cancel',
    editRemoved: 'Remove filter: {field}',
    editChanged: 'Change filter: {field}',
    crmNoteAll: 'Your CRM records are not affected. Only your chat history is removed (and the temporary "Open in CRM" view, if you used it).',
    sumMOppCount: 'Open opportunities',
    sumMOppTotal: 'Total value',
    sumMLastContact: 'Last contact',
    sumMCaseCount: 'Open cases',
    sumMCaseOldest: 'Oldest open case',
    sumMNoPriority: 'No priority',
    sumMContractCount: 'Active contracts',
    sumMContractExpiring: 'Ending within {n} days',
    sumMOrderCount: 'Open orders',
    sumMNone: 'none',
    language: 'Language',
    languageFailed: 'Could not save the language. Please try again.',
    languageWait: 'The language cannot be changed while an answer is being prepared.'
  };

  var T_TR = {
    title: 'CRM\'inize sorun',
    openAdmin: 'Yönetim panelini aç',
    signInFailed: 'CRM üzerinden oturum açılamadı. Sayfayı yenileyin; sürerse yöneticinize haber verin.',
    chartView: 'Grafik',
    tableView: 'Tablo',
    chartPng: 'PNG indir',
    chartPartial: 'Grafik kısmi veri gösteriyor: sonuç kesildi.',
    presentTable: 'Tabloyu gösteriyorum.',
    presentChart: 'Grafiği gösteriyorum.',
    presentLineNote: 'Çizgi grafik tarihe göre sonuçlarda kullanılır; bu sonuç kategoriye göre olduğu için çubuk grafik gösteriyorum.',
    presentBarNote: 'Bu sonuç tarihe göre olduğu için çizgi grafik gösteriyorum.',
    presentNothing: 'Henüz gösterilecek bir cevap yok. Önce bir soru sorun; sonra sonucu grafik ya da tablo olarak gösterebilirim.',
    chartNoneFew: 'Grafik çizilemedi: grafik için en az iki kategori gerekir, bu sonuçta daha azı var.',
    chartNoneList: 'Grafik çizilemedi: bu sonuç bir kayıt listesi. Bir şeye göre gruplanmış sayı ya da toplam isteyin, örneğin duruma ya da şehre göre.',
    chartNoneUnsupported: 'Grafik çizilemedi: bu sonuç grafiğe uygun değil (grafik, bir kategoriye ya da tarihe göre gruplanmış tek bir ölçüyü gösterir).',
    chartNoneChain: 'Grafik çizilemedi: birkaç adımda cevaplanan sorularda grafik gösterilmez.',
    chartOmitted: '{n} kategori daha yalnızca tabloda.',
    chartOther: 'Diğer',
    chartEmpty: '(boş)',
    chartLine: 'Çizgi grafik',
    chartBar: 'Çubuk grafik',
    newChat: 'Yeni sohbet',
    newChatTitle: 'Yeni sohbet',
    searchChats: 'Sohbetlerde ara',
    noChats: 'Henüz sohbet yok',
    noMatch: 'Eşleşen sohbet yok',
    groupPinned: 'Sabitlenenler',
    groupToday: 'Bugün',
    groupYesterday: 'Dün',
    groupWeek: 'Önceki 7 gün',
    groupOlder: 'Daha eski',
    menuRename: 'Yeniden adlandır',
    menuPin: 'Sabitle',
    menuUnpin: 'Sabitlemeyi kaldır',
    menuDelete: 'Sil',
    options: 'Seçenekler',
    toggleSidebar: 'Sohbet listesini göster veya gizle',
    minimize: 'Küçült',
    maximize: 'Tam ekran',
    restore: 'Boyutu geri al',
    close: 'Kapat',
    resize: 'Boyutlandırmak için sürükleyin',
    setBack: '← Sohbete dön',
    setSecAppearance: 'Görünüm',
    setSecChat: 'Sohbet',
    setSecProfile: 'Profil ve dil',
    setSecPrivacy: 'Gizlilik ve veri',
    setSecHelp: 'Yardım ve kısayollar',
    setTheme: 'Tema',
    setTextSize: 'Yazı boyutu',
    sizeSmall: 'Küçük',
    sizeNormal: 'Normal',
    sizeLarge: 'Büyük',
    setDensity: 'Tablo yoğunluğu',
    densityComfortable: 'Rahat',
    densityCompact: 'Sıkı',
    setResetWindow: 'Pencere boyutunu sıfırla',
    setResetDone: 'Pencere boyutu sıfırlandı.',
    setPreviewRows: 'Sohbet tablosunda gösterilen satır',
    setExplainOpen: 'Sorunun nasıl anlaşıldığını hep göster',
    setSuggestions: 'Önerilen sonraki adımları göster',
    setChatNote: 'Bu tercihler bu tarayıcıda saklanır.',
    setNameSaved: 'Ad kaydedildi.',
    setDownload: 'Bu sohbeti indir',
    setDownloadNone: 'Önce bir sohbet açın.',
    setDlYou: 'Siz',
    setDlBot: 'Asistan',
    setDataTitle: 'Verileriniz',
    setPrivacyNote: 'Sonuç satırları saklanmaz; yalnız sorularınız ve cevapların arkasındaki sorgular saklanır.',
    setShortcuts: 'Klavye kısayolları',
    shortcutSend: 'Enter: soruyu gönder',
    shortcutCmd: '/: komut menüsünü aç',
    shortcutTab: 'Tab: seçili komutu tamamla',
    shortcutEsc: 'Esc: menü ya da pencereyi kapat',
    setCommands: 'Komutlar',
    themeLight: 'Açık',
    themeDark: 'Koyu',
    themeAuto: 'Otomatik',
    loading: 'Yükleniyor…',
    loadingEarlier: 'Önceki mesajlar yükleniyor…',
    startOfChat: 'Sohbetin başı',
    retry: 'Yeniden dene',
    loadFailed: 'Yüklenemedi.',
    chatGone: 'Bu sohbet artık yok.',
    show: 'Göster',
    running: 'Çalışıyor…',
    greeting: 'Ne öğrenmek istersiniz?',
    greetingNamed: 'Merhaba {name}, ne öğrenmek istersiniz?',
    nameTitle: 'Size nasıl hitap edelim?',
    nameHint: 'Karşılamada gösterilir. Yalnızca siz görürsünüz. Kaldırmak için boş bırakın.',
    namePlaceholder: 'Adınız',
    nameFailed: 'Adınız kaydedilemedi.',
    save: 'Kaydet',
    greetingHint: 'Sade bir dille sorun. "Bunlardan yalnızca 50 bini aşanlar" gibi devam soruları aynı sohbette sürer.',
    examples: ['Londra\'daki hesapları göster', 'Bu müşterinin açık fırsatlarını göster', 'Tüm ürünleri göster'],
    placeholder: 'Bir soru sorun…',
    placeholderExample: 'örn. {text}',
    ask: 'Sor',
    thinking: 'Düşünüyor…',
    unreachable: 'Servise ulaşılamadı ({msg}).',
    chip: 'Açık kayıt: {entity} ("bu müşteri" gibi sorular buna atıf yapar)',
    chipOff: 'Açık kaydı yok say',
    about: 'Konu: {name}',
    glossaryLine: '{term} (sözlük): {text}',
    retentionDays: 'Sohbetler {n} gün sonra silinir (sabitlenenler korunur).',
    retentionNone: 'Sohbetler otomatik silinmez.',
    auditNotice: 'Sorularınız denetim amacıyla yöneticiler tarafından görüntülenebilir.',
    settings: 'Ayarlar',
    deleteAllMenu: 'Tüm sohbet geçmişini sil…',
    renameFailed: 'Sohbet yeniden adlandırılamadı.',
    pinFailed: 'Sabitleme değiştirilemedi.',
    cancel: 'Vazgeç',
    del: 'Sil',
    deleteAll: 'Tümünü sil',
    deleting: 'Siliniyor…',
    deleteFailed: 'Silinemedi. Tekrar deneyin.',
    deleteChatTitle: 'Bu sohbet silinsin mi?',
    deleteChatBody: '"{name}" ve soruları kalıcı olarak silinecek. Bu işlem geri alınamaz.',
    deleteChatPinned: 'Bu sohbet sabitlenmiş; silmek sabitlemeyi de kaldırır.',
    deleteAllTitle: 'Tüm sohbet geçmişi silinsin mi?',
    deleteAllBody: '{n} sohbet kalıcı olarak silinecek. Sabitlenmiş {p} sohbetiniz korunur. Bu işlem geri alınamaz.',
    deleteAllBodyNoPinned: '{n} sohbet kalıcı olarak silinecek. Bu işlem geri alınamaz.',
    deleteAllBodyWithPinned: '{n} sohbet, sabitlenmiş {p} sohbet dahil, kalıcı olarak silinecek. Bu işlem geri alınamaz.',
    deleteAllIncludePinned: 'Sabitlenmiş {p} sohbetimi de sil',
    deleteAllNothing: 'Silinecek bir şey yok.',
    crmNote: 'CRM verileriniz etkilenmez; yalnızca sohbet geçmişiniz silinir.',
    fetchXml: 'Üretilen FetchXML',
    copy: 'Kopyala',
    copied: 'Kopyalandı',
    truncated: 'Sonuç 500 satırda kesildi — sorunuzu daraltın.',
    countOf: 'Toplam {total} {entity}kaydı var; ilk {shown} tanesi gösteriliyor — sorunuzu daraltın.',
    countMore: '{total} üzerinde {entity}kaydı var; ilk {shown} tanesi gösteriliyor — sorunuzu daraltın.',
    countAll: 'Tüm {total} {entity}kaydı gösteriliyor.',
    suggestionsTitle: 'Sonraki:',
    noRecords: 'Kayıt bulunamadı.',
    stepLabel: 'Adım {n}: {question}',
    stepNoRows: 'kayıt bulunamadı',
    stepMore: '… ve {n} tane daha',
    sumTitle: '{name} özeti',
    sumRefresh: 'Yenile',
    sumVia: '{title} ({via} üzerinden)',
    sumOpen: 'Aç',
    sumOpenRecord: 'Kaydı aç',
    sumEmpty: 'Kayıt yok.',
    sumUnavailable: 'Bu bölüm yüklenemedi.',
    sumBasic: 'Ayrıntılar',
    sumOpportunities: 'Fırsatlar',
    sumActivities: 'Son aktiviteler',
    sumCases: 'Vakalar',
    sumContracts: 'Sözleşmeler',
    sumOrders: 'Siparişler',
    sumFooter: 'Sayılar ve listeler yalnızca CRM\'de görmenize izin verilenleri içerir.',
    draftTitle: '{name} için e-posta taslağı',
    draftWarning: 'Taslak: göndermeden önce kontrol edin. Hiçbir şey gönderilmedi.',
    createTitle: '{entity} oluştur',
    createDoneTitle: '{entity}: sonuç',
    createCancelled: 'Vazgeçildi. Hiçbir şey oluşturulmadı.',
    createAskMissing: 'Bunu oluşturmak için şunlar da lazım: {fields}. Eksik bilgileri sohbete yazarak cevapla.',
    createAskFix: 'Bazı bilgilerin düzeltilmesi gerekiyor. Düzeltilmiş değerleri sohbete yazarak cevapla.',
    createRowsSummary: '{total} kaydın {ready} tanesi hazır.',
    createButton: 'Oluştur',
    createButtonN: '{n} kaydı oluştur',
    createAnyway: 'Yine de oluştur',
    createCancel: 'Vazgeç',
    createOpen: 'Aç',
    createWorking: 'Çalışıyor…',
    createDuplicate: 'Bu adla bir kayıt zaten var: {names}.',
    createStatus_ready: 'Hazır',
    createStatus_needs_info: 'Bilgi lazım',
    createStatus_created: 'Oluşturuldu',
    createStatus_failed: 'Başarısız',
    createStatus_skipped: 'Oluşturulmadı',
    createErr_permission: 'Bu kaydı oluşturma yetkin yok.',
    createErr_crm: 'CRM bu kaydı oluşturamadı.',
    createProb_missing_required: '{label} zorunlu.',
    createProb_unknown_field: '“{label}” alanını tanımıyorum.',
    createProb_invalid_number: '{label} bir sayı olmalı.',
    createProb_invalid_date: '{label} bir tarih olmalı, örneğin 2026-10-26.',
    createProb_invalid_boolean: '{label} evet ya da hayır olmalı.',
    createProb_too_long: '{label} çok uzun (en çok {detail} karakter).',
    createProb_choice_not_found: '{label}: şunlardan birini seç: {detail}.',
    createProb_lookup_not_found: '{label}: “{detail}” adında kayıt bulunamadı.',
    createProb_lookup_ambiguous: '{label}: birden çok kayıt eşleşiyor ({detail}). Daha açık yaz.',
    draftTo: 'Kime:',
    draftSubject: 'Konu:',
    draftNoRecipient: '(kayıtta e-posta adresi yok)',
    draftMailto: 'E-posta uygulamasında aç',
    draftMailtoTooLong: 'Bu taslak e-posta uygulaması bağlantısı için çok uzun. Bunun yerine Kopyala\'yı kullanın.',
    draftAgain: 'Yeniden yaz',
    draftUnresolved: 'Vurgulanan öğeler CRM\'den doldurulamadı. Kendiniz doldurun.',
    draftCopyFailed: 'Otomatik kopyalanamadı. Metin seçili: Ctrl+C\'ye basın.',
    fbUp: 'İyi cevap',
    fbDown: 'Yardımcı olmadı',
    fbReasonTitle: 'Ne yanlış gitti? (isteğe bağlı)',
    fbReasonWrongRecords: 'Yanlış ya da eksik kayıtlar',
    fbReasonWrongColumns: 'Yanlış sütunlar',
    fbReasonMisunderstood: 'Sorumu anlamadı',
    fbReasonTooSlow: 'Çok yavaş',
    fbReasonOther: 'Başka bir şey',
    fbShare: 'Aracın gelişmesine yardımcı olmak için bu soruyu yöneticinizle paylaşın.',
    fbShareWarn: 'Sorunuz kişisel bilgi içeriyorsa paylaşmayın. Açıklamadaki kayıt adları da paylaşılır.',
    fbSend: 'Gönder',
    fbThanks: 'Geri bildiriminiz için teşekkürler.',
    fbShared: 'Yöneticinizle paylaşıldı.',
    fbWithdraw: 'Geri çek',
    fbWithdrawn: 'Paylaşım geri çekildi.',
    fbFailed: 'Geri bildiriminiz kaydedilemedi. Tekrar deneyin.',
    qsAdd: 'Kabul setine ekle',
    qsTitle: 'Bu cevabı doğru örnek olarak kaydedin. Şu an {n} satır; satırların kendisi saklanmaz.',
    qsOrderTitle: 'Satırlar nasıl karşılaştırılsın?',
    qsOrderAuto: 'Soru sıralıyorsa aynı sırada',
    qsOrderOrdered: 'Her zaman aynı sırada',
    qsOrderUnordered: 'Sıra önemli değil',
    qsColsTitle: 'Hangi sütunlar sayılsın?',
    qsColsRef: 'Bu sütunlar; fazladan sütun olabilir',
    qsColsExact: 'Tam olarak bu sütunlar',
    qsConfirm: 'Bu cevabı kontrol ettim, doğru',
    qsSave: 'Kaydet',
    qsCancel: 'Vazgeç',
    qsSaved: 'Kabul setine kaydedildi.',
    qsDuplicate: 'Bu cevap kabul setinde zaten var.',
    sgAction: 'İyi örnek olarak öner',
    sgNotice: 'Sorunuz ve onu cevaplayan sorgu, yöneticilere test örneği olarak gösterilecek. Yalnızca test için kullanılır: kimsenin adı eklenmez ve sonuç satırları asla paylaşılmaz. Bir yönetici onaylamadan hiçbir şey çalışmaz.',
    sgSend: 'Öneriyi gönder',
    sgDone: 'Teşekkürler. Bir yönetici önerinizi inceleyecek.',
    sgDuplicate: 'Benzer bir örnek zaten var.',
    sgFailed: 'Öneri gönderilemedi.',
    rpFound: 'İsteğinize uyan raporlar',
    rpAll: 'Açabileceğiniz raporlar (belirli bir rapor için adını yazın)',
    rpOpen: 'Raporu aç',
    rpFor: 'Kayıt: {name}',
    rpHint: 'Rapor CRM\'de yeni sekmede açılır; süzgeçlerini orada seçersiniz.',
    qsFailed: 'Bu cevap kaydedilemedi. Tekrar deneyin.',
    fbDeleteNote: 'Bu sohbetten yöneticinizle paylaştığınız sorular da geri çekilir.',
    openAll: '{n} satırın tümünü yeni sekmede aç',
    openRecord: 'Aç',
    openRecordHint: 'Bu kaydı CRM\'de aç',
    openInCrm: 'CRM\'de aç',
    openInCrmHint: 'Tüm satırları CRM\'in kendi liste görünümünde açar (sayfalama, sıralama, dışa aktarma)',
    openingInCrm: 'Açılıyor…',
    openInCrmFailed: 'CRM\'de açılamadı. Bunun yerine tüm satırları yeni sekmede açma bağlantısını kullanın.',
    openInCrmDenied: 'CRM\'de görünüm oluşturma izniniz yok. Bunun yerine tüm satırları yeni sekmede açma bağlantısını kullanın.',
    searchResults: 'Sonuçlarda ara…',
    rowsOf: ' {a} / {b} satır',
    prev: 'Önceki',
    next: 'Sonraki',
    pageOf: 'Sayfa {a} / {b}',
    columnsHint: 'ya da sütun adlarını yazın, örn. id, name',
    rephraseHint: 'ya da sorunuzu başka şekilde yazın',
    send: 'Gönder',
    stop: 'Durdur',
    stopped: 'Durduruldu',
    cmdHelp: 'Neler sorabileceğinizi ve komutları göster',
    cmdNew: 'Yeni sohbet başlat',
    cmdEdit: 'Son sorunuzu değiştirmek için kutuya geri koy',
    cmdHistory: 'Sohbet listesini aç ve ara',
    cmdClear: 'Sohbet geçmişinizi sil',
    noSuchCommand: 'Böyle bir komut yok. /help yazın.',
    helpTitle: 'Burada neler yapabilirsiniz',
    helpCommands: 'Komutlar',
    helpExamples: 'Örnek sorular (kullanmak için tıklayın)',
    editRemove: 'Bu süzgeci kaldır',
    editChange: 'Bu süzgeci değiştir',
    editApply: 'Uygula',
    editCancel: 'Vazgeç',
    editRemoved: 'Süzgeci kaldır: {field}',
    editChanged: 'Süzgeci değiştir: {field}',
    crmNoteAll: 'CRM kayıtlarınız etkilenmez. Yalnızca sohbet geçmişiniz silinir (kullandıysanız geçici "CRM\'de aç" görünümü de).',
    sumMOppCount: 'Açık fırsatlar',
    sumMOppTotal: 'Toplam değer',
    sumMLastContact: 'Son temas',
    sumMCaseCount: 'Açık vakalar',
    sumMCaseOldest: 'En eski açık vaka',
    sumMNoPriority: 'Öncelik yok',
    sumMContractCount: 'Aktif sözleşmeler',
    sumMContractExpiring: '{n} gün içinde bitenler',
    sumMOrderCount: 'Açık siparişler',
    sumMNone: 'yok',
    language: 'Dil',
    languageFailed: 'Dil kaydedilemedi. Lütfen tekrar deneyin.',
    languageWait: 'Bir cevap hazırlanırken dil değiştirilemez.'
  };
  var CATALOGS = { en: T_EN, tr: T_TR };
  var LANGUAGES = ['en', 'tr'];
  var LANGUAGE_NAMES = { en: 'English', tr: 'Türkçe' };   // i18n-ok
  var currentLanguage = 'en';

  // BEGIN pickLanguage
  /** Picks the startup UI language from the saved choice, then the browser language, falling back to English. */
  function pickLanguage(saved, browser, supported) {
    var primary = function (value) { return String(value || '').toLowerCase().split(/[-_]/)[0]; };
    if (supported.indexOf(primary(saved)) >= 0 && saved) { return primary(saved); }
    if (supported.indexOf(primary(browser)) >= 0 && browser) { return primary(browser); }
    return 'en';
  }
  // END pickLanguage

  /** Returns the translated text for a key in the current language. */
  function tv(key) {
    var table = CATALOGS[currentLanguage];
    return table && table[key] !== undefined ? table[key] : T_EN[key];
  }

  /** Returns the browser locale tag matching the current widget language. */
  function currentLocale() { return currentLanguage === 'tr' ? 'tr-TR' : 'en-US'; }

  /** Looks up a translated string by key and fills in the given variables. */
  function t(key, vars) {
    var s = tv(key);
    if (s === undefined) { return key; }
    if (vars) { s = s.replace(/\{(\w+)\}/g, function (m, k) { return vars[k] === undefined ? m : String(vars[k]); }); }
    return s;
  }

  var CSS = [
    ':host { all: initial; }',
    '*, *::before, *::after { box-sizing: border-box; }',
    '.root { position: fixed; right: 16px; bottom: 16px; z-index: 2147483647; font: 13px "Segoe UI", Arial, sans-serif; color: #222; }',
    '.fab { width: 52px; height: 52px; border-radius: 50%; border: 0; background: #0b5cad; color: #fff; font-size: 22px; cursor: pointer; box-shadow: 0 2px 10px rgba(0,0,0,.35); }',
    '.panel { display: none; position: relative; flex-direction: column; width: 860px; height: 78vh; max-width: 96vw; max-height: 96vh; background: #fff; border: 1px solid #0b5cad; border-radius: 8px; box-shadow: 0 4px 18px rgba(0,0,0,.4); overflow: hidden; }',
    '.root.open .panel { display: flex; } .root.open .fab { display: none; }',
    '.root.max { right: 8px; bottom: 8px; left: 8px; top: 8px; }',
    '.root.max .panel { width: 100%; height: 100%; max-width: none; max-height: none; }',
    '.panel.min { height: auto !important; }',
    '.panel.min .body { display: none; }',
    '.head { display: flex; align-items: center; gap: 6px; background: #0b5cad; color: #fff; padding: 6px 8px; font-weight: 600; }',
    '.head button { background: transparent; border: 0; color: #fff; font-size: 16px; cursor: pointer; padding: 2px 6px; border-radius: 4px; }',
    '.head button:hover { background: rgba(255,255,255,.18); }',
    '.head .title { flex: 1; min-width: 0; font: inherit; color: #fff; background: transparent; border: 1px solid transparent; border-radius: 4px; padding: 2px 6px; }',
    '.head .title:not([readonly]):hover, .head .title:focus { border-color: rgba(255,255,255,.55); outline: none; background: rgba(255,255,255,.12); }',
    '.resize { position: absolute; left: 0; top: 0; width: 14px; height: 14px; cursor: nwse-resize; z-index: 3; }',
    '.panel.min .resize, .root.max .resize { display: none; }',
    '.body { flex: 1; display: flex; min-height: 0; position: relative; }',
    '.side { width: 250px; flex: none; background: #f3f5f8; border-right: 1px solid #d8dee4; display: flex; flex-direction: column; }',
    '.side.closed { display: none; }',
    '.narrow .side { position: absolute; z-index: 4; top: 0; bottom: 0; left: 0; box-shadow: 2px 0 10px rgba(0,0,0,.25); }',
    '.newchat { margin: 10px; padding: 7px 10px; border: 1px solid #0b5cad; color: #0b5cad; background: #fff; border-radius: 6px; cursor: pointer; font: inherit; font-weight: 600; text-align: left; }',
    '.newchat:hover { background: #eaf2fb; }',
    '.search { margin: 0 10px 8px; padding: 5px 8px; border: 1px solid #c3cbd3; border-radius: 4px; font: inherit; }',
    '.list { flex: 1; overflow: auto; padding: 0 6px 8px; }',
    '.group { font-size: 11px; color: #778; text-transform: uppercase; letter-spacing: .04em; margin: 10px 6px 4px; }',
    '.item { position: relative; display: flex; align-items: center; gap: 4px; padding: 6px 8px; border-radius: 6px; cursor: pointer; }',
    '.item:hover { background: #e4eaf1; } .item.active { background: #dcebfa; font-weight: 600; }',
    '.item .t { flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }',
    '.item .dots { visibility: hidden; border: 0; background: transparent; cursor: pointer; font-size: 15px; color: #556; border-radius: 4px; padding: 0 4px; }',
    '.item:hover .dots, .item.active .dots, .item.menu-open .dots { visibility: visible; } .item .dots:hover { background: #cfd8e2; }',
    '.menu { position: absolute; right: 6px; top: 30px; z-index: 6; background: #fff; border: 1px solid #c3cbd3; border-radius: 6px; box-shadow: 0 3px 10px rgba(0,0,0,.2); min-width: 140px; padding: 4px; font-weight: 400; }',
    '.menu button { display: block; width: 100%; text-align: left; border: 0; background: transparent; padding: 5px 8px; font: inherit; cursor: pointer; border-radius: 4px; }',
    '.menu button:hover { background: #eef3f8; } .menu .danger { color: #b00020; }',
    '.rename { width: 100%; font: inherit; padding: 2px 4px; border: 1px solid #0b5cad; border-radius: 4px; }',
    '.note { font-size: 11px; color: #667; padding: 6px 10px; border-top: 1px solid #d8dee4; }',
    '.main { flex: 1; display: flex; flex-direction: column; min-width: 0; }',
    '.log { flex: 1; overflow: auto; padding: 10px; background: #f6f8fa; }',
    '.msg { margin: 0 0 10px; }',
    '.you { background: #dcebfa; padding: 6px 10px; border-radius: 6px; display: inline-block; max-width: 92%; white-space: pre-wrap; }',
    '.bot { background: #fff; border: 1px solid #d8dee4; padding: 8px 10px; border-radius: 6px; }',
    '.error { color: #b00020; } .warn { color: #8a5a00; } .question { font-weight: 600; } .muted { color: #666; }',
    '.empty { text-align: center; color: #667; margin-top: 60px; } .empty h2 { margin: 0 0 6px; color: #234; font-size: 17px; }',
    '.stepbox { margin: 4px 0; padding: 6px 8px; background: #f3f7fb; border-left: 3px solid #0b5cad; font-size: 12px; } .stepbox .stepline { margin-left: 10px; }',
    '.narrow { display: flex; flex-wrap: wrap; gap: 8px; align-items: center; margin: 6px 0; }',
    '.narrowgroup { display: inline-flex; flex-wrap: wrap; gap: 4px; align-items: center; } .narrowlabel { color: #445; font-size: 12px; }',
    '.explain { margin: 4px 0; } .explain summary { cursor: pointer; color: #445; } .explainline { padding: 1px 0 1px 14px; color: #334; }',
    '.sum { margin: 2px 0; } .sumhead { font-weight: 600; margin-bottom: 4px; } .sumsec { margin: 6px 0; padding: 6px 8px; background: #f7f9fb; border: 1px solid #dde3ea; border-radius: 6px; } .sumsec summary { cursor: pointer; font-weight: 600; } .sumline { padding: 1px 0 1px 12px; color: #334; } .sumline .metric { color: #555; } .sumitem { padding: 1px 0 1px 12px; }',
    '.create { margin: 2px 0; } .crow { margin: 6px 0; padding: 8px; background: #f7f9fb; border: 1px solid #dde3ea; border-radius: 6px; } .cname { margin-bottom: 2px; } .cfield { padding: 1px 0; } .cfield .metric { color: #555; } .cprob { color: #a15c00; padding-top: 2px; } .cok { color: #1b7f3b; } .cwarn { color: #a15c00; } .cbad { color: #b42318; } .cbtns { display: flex; flex-wrap: wrap; gap: 6px; align-items: center; margin-top: 6px; }',
    '.draft { margin: 2px 0; } .draftwarn { margin: 2px 0 6px; } .draftline { padding: 1px 0; } .draftline .metric { color: #555; } .draftbody { white-space: pre-wrap; margin: 6px 0; padding: 8px; background: #f7f9fb; border: 1px solid #dde3ea; border-radius: 6px; user-select: text; } .mark { background: #fff1b8; border-radius: 3px; padding: 0 2px; } .draftbtns { display: flex; flex-wrap: wrap; gap: 6px; align-items: center; }',
    '.langrow { display: flex; align-items: center; gap: 6px; padding: 4px 10px; } .langrow button { border: 1px solid #c3cbd3; background: #fff; border-radius: 4px; padding: 2px 8px; cursor: pointer; font: inherit; } .langrow .langon { background: #e7f0fa; border-color: #0b5cad; font-weight: 600; }',
    '.answerfoot { display: flex; flex-wrap: wrap; align-items: center; gap: 4px 10px; margin-top: 6px; } .answerfoot .narrow { margin: 0; } .answerfoot .fb { margin: 0 0 0 auto; } .answerfoot .fb:has(.fbform) { flex-basis: 100%; margin-left: 0; }',
    '.fb { margin-top: 6px; } .fbrow { display: flex; align-items: center; gap: 6px; flex-wrap: wrap; } .fbbtn { margin-left: 0; opacity: .55; font-size: 15px; } .fbon { opacity: 1; background: #e7f0fb; border-radius: 4px; }',
    '.fbform { margin: 6px 0 2px; padding: 8px; border: 1px solid #c3cbd3; border-radius: 6px; background: #f7f9fb; } .fbform label { display: flex; gap: 6px; align-items: flex-start; margin: 4px 0; }',
    '.xbtn { margin-left: 6px; border: 0; background: transparent; cursor: pointer; color: #0b5cad; font: inherit; } .editbox { display: inline-flex; gap: 6px; align-items: center; flex-wrap: wrap; }',
    '.chips { display: flex; flex-wrap: wrap; gap: 6px; justify-content: center; margin-top: 12px; }',
    '.chipbtn { border: 1px solid #aab4be; background: #fff; border-radius: 14px; padding: 4px 10px; cursor: pointer; font: inherit; } .chipbtn:hover { background: #eef3f8; }',
    '.choices { display: flex; flex-wrap: wrap; gap: 6px; margin: 6px 0 0; }',
    'button.btn { padding: 4px 10px; border: 1px solid #aab4be; background: #fff; border-radius: 4px; cursor: pointer; font: inherit; }',
    'button.btn:hover { background: #eef3f8; } button.btn:disabled { opacity: .5; cursor: default; }',
    'button.btn.primary { background: #0b5cad; border-color: #0b5cad; color: #fff; } button.btn.primary:hover { background: #094a8c; }',
    'button.btn.danger { background: #b00020; border-color: #b00020; color: #fff; } button.btn.danger:hover { background: #930019; }',
    'input.txt { padding: 5px 8px; border: 1px solid #aab4be; border-radius: 4px; font: inherit; }',
    '.chartbar { display: flex; gap: 6px; margin: 6px 0; } .chartbar button[aria-pressed="true"] { background: #0b5cad; border-color: #0b5cad; color: #fff; } .chartbox { margin: 6px 0; }',
    '.tablewrap { overflow: auto; max-height: 280px; margin: 6px 0; }',
    'table { border-collapse: collapse; } th, td { border: 1px solid #ccc; padding: 3px 8px; text-align: left; white-space: nowrap; }',
    'th { background: #f0f4f8; position: sticky; top: 0; }',
    'pre { background: #f5f5f5; padding: 6px; overflow: auto; margin: 6px 0; }',
    '.pager { display: flex; gap: 8px; align-items: center; margin-top: 4px; }',
    '.chip { align-items: center; justify-content: space-between; gap: 6px; padding: 4px 10px; font-size: 12px; color: #444; background: #eef6e8; border-top: 1px solid #d8dee4; }',
    '.foot { display: flex; gap: 6px; padding: 8px; border-top: 1px solid #d8dee4; background: #fff; position: relative; }',
    '.foot input { flex: 1; }',
    '.cmdmenu { display: none; position: absolute; left: 8px; right: 8px; bottom: 100%; background: #fff; border: 1px solid #aab4be; border-radius: 6px; box-shadow: 0 -2px 8px rgba(0,0,0,.12); max-height: min(240px, 40vh); overflow: auto; z-index: 5; }',
    '.cmdrow { display: flex; gap: 10px; align-items: baseline; padding: 6px 10px; cursor: pointer; } .cmdrow.sel, .cmdmenu .cmdrow:hover { background: #eef3f8; }',
    '.cmdname { font-weight: 600; color: #0b5cad; min-width: 64px; } .cmdnone { padding: 6px 10px; color: #667; }',
    '.helplist { margin: 4px 0 8px; } .helplist .cmdrow { cursor: default; padding: 2px 0; }',
    '.sk { height: 14px; margin: 10px 8px; border-radius: 4px; background: linear-gradient(90deg,#e3e8ee 25%,#f1f4f7 37%,#e3e8ee 63%); background-size: 400% 100%; animation: sk 1.2s ease infinite; }',
    '@keyframes sk { 0% { background-position: 100% 0; } 100% { background-position: 0 0; } }',
    '.spin { display: inline-block; width: 12px; height: 12px; margin-right: 6px; vertical-align: -2px; border: 2px solid #b8c2cc; border-top-color: #0b5cad; border-radius: 50%; animation: rot .8s linear infinite; }',
    '@keyframes rot { to { transform: rotate(360deg); } }',
    '.edge { text-align: center; color: #778; font-size: 12px; padding: 4px 0 10px; }',
    '.layer { position: absolute; inset: 0; z-index: 8; background: rgba(20,30,40,.45); display: flex; align-items: center; justify-content: center; }',
    '.dialog { background: #fff; border-radius: 8px; padding: 16px; width: 380px; max-width: 92%; box-shadow: 0 6px 24px rgba(0,0,0,.35); }',
    '.dialog h3 { margin: 0 0 8px; font-size: 15px; } .dialog p { margin: 0 0 8px; } .dialog .small { font-size: 12px; color: #667; }',
    '.dialog label { display: flex; gap: 6px; align-items: center; margin: 8px 0; }',
    '.dialog .row { display: flex; justify-content: flex-end; gap: 8px; margin-top: 12px; }',
    '.toast { position: absolute; left: 50%; bottom: 70px; transform: translateX(-50%); background: #223; color: #fff; padding: 6px 12px; border-radius: 6px; font-size: 12px; z-index: 9; }',
    '.root { --primary: #2457f5; --primary-hover: #1c46d0; --primary-soft: #eef3ff; --primary-soft2: #dbe5ff; --ink: #111b38; --canvas: #f3f5fa; --card: #fff; --soft: #f7f9fd; --hover: #eef1f8; --border: #dfe4ee; --border-strong: #c5cddd; --text: #1b2338; --muted: #5f6b85; --link: #1c46d0; --danger: #c92a2a; --danger-solid: #c92a2a; --ok: #1b7f3b; --warn-text: #7a5200; --warn-bg: #fff7e6; --warn-border: #f1d9a8; --mark: #fff1b8; --shadow: 0 8px 28px rgba(17,27,56,.22); }',
    '.root.dark { --primary: #4a7aff; --primary-hover: #6690ff; --primary-soft: #1a2547; --primary-soft2: #243160; --ink: #0d1530; --canvas: #0a1020; --card: #141c33; --soft: #0f1629; --hover: #1c2640; --border: #263150; --border-strong: #344060; --text: #d5dbea; --muted: #8793b3; --link: #8dabff; --danger: #ff8a8a; --ok: #6fd08c; --warn-text: #f0d08a; --warn-bg: #3a2f12; --warn-border: #6b5320; --mark: #5c4a00; --shadow: 0 8px 28px rgba(0,0,0,.6); }',
    '.root { font: 13.5px/1.5 "IBM Plex Sans", "Segoe UI", system-ui, -apple-system, Arial, sans-serif; color: var(--text); font-feature-settings: "tnum" 1; }',
    '.fab { display: flex; align-items: center; justify-content: center; width: 56px; height: 56px; background: var(--primary); color: #fff; box-shadow: 0 6px 18px rgba(36,87,245,.4); transition: transform .15s, background .15s; } .fab:hover { transform: translateY(-2px); background: var(--primary-hover); } .fab svg { width: 27px; height: 27px; }',
    '.panel { background: var(--card); border: 1px solid var(--border); border-radius: 10px; box-shadow: var(--shadow); }',
    '.head { background: var(--ink); color: #fff; padding: 10px 12px; gap: 8px; } .head button { border-radius: 6px; padding: 4px 9px; opacity: .92; } .head .title { font-size: 14px; font-weight: 600; } .head .active { background: rgba(255,255,255,.22); }',
    '.side { width: 264px; background: var(--canvas); border-right: 1px solid var(--border); }',
    '.newchat { margin: 12px; padding: 8px 12px; border: 0; border-radius: 6px; background: var(--primary); color: #fff; text-align: center; } .newchat:hover { background: var(--primary-hover); }',
    '.search { margin: 0 12px 10px; padding: 7px 11px; border: 1px solid var(--border-strong); border-radius: 6px; background: var(--card); color: var(--text); } .search:focus, .rename:focus, input.txt:focus { outline: none; border-color: var(--primary); box-shadow: 0 0 0 3px var(--primary-soft2); }',
    '.group { margin: 14px 10px 6px; font-size: 10.5px; font-weight: 600; letter-spacing: .06em; color: var(--muted); }',
    '.item { padding: 7px 10px; border-radius: 6px; } .item:hover { background: var(--hover); } .item.active { background: var(--primary-soft); color: var(--link); font-weight: 600; } .item .dots { color: var(--muted); } .item .dots:hover { background: var(--primary-soft2); }',
    '.rename { background: var(--card); color: var(--text); border-color: var(--primary); }',
    '.menu, .cmdmenu { background: var(--card); color: var(--text); border: 1px solid var(--border); border-radius: 10px; box-shadow: var(--shadow); } .menu button { color: var(--text); border-radius: 6px; padding: 6px 10px; } .menu button:hover, .cmdrow.sel, .cmdmenu .cmdrow:hover { background: var(--hover); } .menu .danger { color: var(--danger); } .cmdname { color: var(--link); } .cmdnone { color: var(--muted); }',
    '.note { color: var(--muted); border-top-color: var(--border); }',
    '.log { padding: 20px 22px; background: var(--canvas); }',
    '.msg { margin: 0 0 16px; } .msg:has(> .you) { display: flex; justify-content: flex-end; }',
    '.you { max-width: 78%; padding: 8px 14px; border-radius: 10px 10px 2px 10px; background: var(--primary); color: #fff; }',
    '.msg:has(> .bot) { display: flex; gap: 10px; align-items: flex-start; } .msg:has(> .bot)::before { content: "N"; flex: none; display: flex; align-items: center; justify-content: center; width: 28px; height: 28px; margin-top: 2px; border-radius: 6px; border: 1px solid var(--border-strong); background: var(--ink); color: #fff; font-size: 13px; font-weight: 700; }',
    '.bot { flex: 1; min-width: 0; padding: 12px 14px; border: 1px solid var(--border); border-radius: 2px 10px 10px 10px; background: var(--card); }',
    '.error { color: var(--danger); } .warn { margin: 6px 0; padding: 6px 10px; border: 1px solid var(--warn-border); border-radius: 6px; background: var(--warn-bg); color: var(--warn-text); } .muted, .edge { color: var(--muted); } .question { color: var(--text); }',
    '.empty { margin-top: 44px; color: var(--muted); } .empty h2 { font-size: 21px; font-weight: 600; color: var(--text); } .chips { gap: 10px; margin-top: 20px; }',
    '.chipbtn { padding: 6px 14px; border: 1px solid var(--border-strong); border-radius: 999px; background: var(--card); color: var(--text); } .chipbtn:hover { background: var(--primary-soft); border-color: var(--primary); } .empty .chipbtn { padding: 10px 16px; border-radius: 10px; text-align: left; }',
    'button.btn, a.btn { display: inline-block; padding: 6px 13px; border: 1px solid var(--border-strong); border-radius: 6px; background: var(--card); color: var(--text); font-weight: 600; text-decoration: none; cursor: pointer; } button.btn:hover, a.btn:hover { background: var(--hover); } button.btn:disabled { opacity: .5; cursor: default; }',
    'button.btn.primary, .foot .btn { background: var(--primary); border-color: var(--primary); color: #fff; } button.btn.primary:hover, .foot .btn:hover { background: var(--primary-hover); border-color: var(--primary-hover); }',
    'button.btn.danger, .foot .btn.danger { background: var(--danger-solid); border-color: var(--danger-solid); color: #fff; }',
    '.tblactions { display: flex; flex-wrap: wrap; gap: 8px; align-items: center; margin-top: 6px; } a { color: var(--link); } .xbtn { color: var(--link); }',
    '.rprow { display: flex; align-items: center; justify-content: space-between; gap: 12px; padding: 8px 0; border-top: 1px solid var(--line, rgba(128,128,128,.25)); } .rprow:first-of-type { border-top: 0; } .rprow .rpinfo { flex: 1; min-width: 0; } .rprow a.btn { flex: none; }',
    'input.txt { padding: 7px 11px; border: 1px solid var(--border-strong); border-radius: 6px; background: var(--card); color: var(--text); }',
    '.foot { gap: 10px; padding: 12px 16px; border-top: 1px solid var(--border); background: var(--card); } .foot input.txt { padding: 10px 14px; border-radius: 8px; background: var(--soft); font-size: 14px; } .foot input.txt:focus { background: var(--card); } .foot .btn { padding: 0 20px; border-radius: 8px; }',
    '.foot .btn.send { display: flex; align-items: center; justify-content: center; flex: none; width: 44px; height: 44px; padding: 0; border-radius: 50%; } .foot .btn.send svg { display: block; }',
    '.tablewrap { margin: 10px 0; border: 1px solid var(--border); border-radius: 10px; }',
    'table { width: 100%; border-collapse: separate; border-spacing: 0; font-size: 12.5px; } th, td { padding: 8px 13px; border: 0; border-bottom: 1px solid var(--border); } th { background: var(--soft); color: var(--muted); font-weight: 600; } tr:last-child td { border-bottom: 0; } tr:hover td { background: var(--hover); }',
    '.explain summary { color: var(--muted); } .explainline, .sumline { color: var(--text); } .metric, .sumline .metric, .cfield .metric, .draftline .metric { color: var(--muted); }',
    '.stepbox { border-left: 3px solid var(--primary); border-radius: 0 6px 6px 0; background: var(--primary-soft); }',
    '.sumsec, .draftbody, .fbform, .crow { border: 1px solid var(--border); border-radius: 10px; background: var(--soft); }',
    '.cok { color: var(--ok); } .cwarn, .cprob { color: var(--warn-text); } .cbad { color: var(--danger); } .mark { background: var(--mark); color: inherit; } .narrowlabel { color: var(--muted); }',
    '.langrow button { border: 1px solid var(--border-strong); background: var(--card); color: var(--text); border-radius: 6px; } .langrow .langon { background: var(--primary-soft); border-color: var(--primary); }',
    '.answerfoot { margin-top: 10px; padding-top: 8px; border-top: 1px solid var(--border); } .fbbtn { border-radius: 6px; } .fbon { background: var(--primary-soft); }',
    '.chip { padding: 6px 14px; border-top: 1px solid var(--border); background: var(--primary-soft); color: var(--link); }',
    '.chartbar button[aria-pressed="true"] { background: var(--primary); border-color: var(--primary); color: #fff; } .root.dark .chartbox { padding: 6px; border-radius: 6px; background: #fff; }',
    'pre { background: var(--soft); color: var(--text); border-radius: 6px; padding: 8px; }',
    '.spin { border-color: var(--border-strong); border-top-color: var(--primary); } .sk { background: linear-gradient(90deg, var(--hover) 25%, var(--soft) 37%, var(--hover) 63%); background-size: 400% 100%; }',
    '.dialog { background: var(--card); color: var(--text); border-radius: 10px; box-shadow: var(--shadow); } .dialog .small { color: var(--muted); } .toast { border-radius: 6px; background: var(--ink); color: #fff; }',
    '.resize { width: 20px; height: 20px; } .resize-l { position: absolute; left: 0; top: 20px; bottom: 0; width: 7px; cursor: ew-resize; z-index: 3; } .resize-t { position: absolute; top: 0; left: 20px; right: 0; height: 7px; cursor: ns-resize; z-index: 3; } .panel.min .resize-l, .panel.min .resize-t, .root.max .resize-l, .root.max .resize-t { display: none; }',
    '.root.fs-sm .body { zoom: .92; } .root.fs-lg .body { zoom: 1.12; } .root.compact th, .root.compact td { padding: 4px 11px; }',
    '.settings { display: none; flex: 1; min-height: 0; background: var(--canvas); } .main.settingsmode .log, .main.settingsmode .chip, .main.settingsmode .foot { display: none; } .main.settingsmode .settings { display: flex; }',
    '.snav { width: 200px; flex: none; padding: 14px 10px; display: flex; flex-direction: column; gap: 4px; border-right: 1px solid var(--border); background: var(--card); } .snav .btn { margin-bottom: 8px; text-align: left; }',
    '.snavbtn { text-align: left; padding: 8px 12px; border: 0; border-radius: 6px; background: transparent; color: var(--muted); font: inherit; cursor: pointer; } .snavbtn:hover { background: var(--hover); } .snavbtn.on { background: var(--primary-soft); color: var(--link); font-weight: 600; }',
    '.spane { flex: 1; min-width: 0; overflow: auto; padding: 22px 26px; } .spane h3 { margin: 0 0 14px; font-size: 16px; font-weight: 600; color: var(--text); } .sset { margin: 0 0 18px; max-width: 520px; } .sset > .slabel { display: block; margin: 0 0 7px; font-weight: 600; color: var(--text); } .sset .small { font-size: 12px; color: var(--muted); margin-top: 6px; }',
    '.seg { display: inline-flex; border: 1px solid var(--border-strong); border-radius: 6px; overflow: hidden; background: var(--card); } .seg button { border: 0; border-right: 1px solid var(--border); padding: 7px 14px; background: transparent; font: inherit; color: var(--text); cursor: pointer; } .seg button:last-child { border-right: 0; } .seg button:hover { background: var(--hover); } .seg button.on { background: var(--primary); color: #fff; font-weight: 600; }',
    '.strow { display: flex; align-items: center; gap: 10px; margin: 0 0 14px; cursor: pointer; } .strow input { width: 16px; height: 16px; }',
    '.sline { display: flex; gap: 8px; align-items: center; flex-wrap: wrap; } .sline input.txt { flex: 1; min-width: 160px; max-width: 280px; }',
    '.shortcuts { margin: 0; padding-left: 18px; line-height: 1.9; }'
  ].join('\n');

  /** Folds text to a case- and accent-insensitive form matching the server's name normalizer, including Turkish dotted and dotless I. */
  function fold(s) {
    return s.replace(/[İIıi]/g, 'i').replace(/[Şş]/g, 's').replace(/[Ğğ]/g, 'g')
      .replace(/[Üü]/g, 'u').replace(/[Öö]/g, 'o').replace(/[Çç]/g, 'c').toLowerCase();
  }

  /** Tells whether the result table carries hidden record ids so each row can get an Open link. */
  function hasRecordLinks(d) {
    return !!(d.table && d.table.rowIds && d.recordUrlBase && /^https?:\/\//i.test(d.recordUrlBase));
  }
  /** Builds the leading table cell holding a small Open link to the row's CRM record. */
  function recordCell(d, index) {
    var td = el('td');
    var id = d.table.rowIds[index];
    if (id) {
      var a = el('a', t('openRecord'));
      a.href = d.recordUrlBase + encodeURIComponent(id);
      a.target = '_blank';
      a.rel = 'noopener noreferrer';
      a.title = t('openRecordHint');
      td.append(a);
    }
    return td;
  }

  /** Creates a DOM element with optional text content and class name. */
  function el(tag, text, cls) {
    var e = document.createElement(tag);
    if (text !== undefined) { e.textContent = text; }
    if (cls) { e.className = cls; }
    return e;
  }

  /** Stops key events in an input from reaching the host CRM page. */
  function stopKeys(input) {
    ['keydown', 'keyup', 'keypress'].forEach(function (n) { input.addEventListener(n, function (e) { e.stopPropagation(); }); });
  }

  /** Copies text to the clipboard, falling back to a hidden textarea and execCommand when the clipboard API is unavailable. */
  function copyText(text) {
    if (navigator.clipboard && window.isSecureContext) {
      navigator.clipboard.writeText(text);
      return;
    }
    var area = document.createElement('textarea');
    area.value = text;
    area.style.cssText = 'position:fixed;left:-9999px;top:0;opacity:0';
    document.body.appendChild(area);
    area.select();
    try { document.execCommand('copy'); } catch (e) {  }
    document.body.removeChild(area);
  }

  // BEGIN parseRecord
  /** Reads the open record's entity type and id from a CRM page address, supporting classic and Unified Interface URLs. */
  function parseRecord(href) {
    try {
      var url = new URL(href);
      var query = new URLSearchParams(url.search);
      var entity = (query.get('etn') || '').toLowerCase();
      var id = (query.get('id') || '').replace(/^{|}$/g, '').toLowerCase();
      if (!/^[a-z][a-z0-9_]{0,63}$/.test(entity)) { return null; }
      if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/.test(id)) { return null; }
      return { entity: entity, id: id };
    } catch (e) { return null; }
  }
  // END parseRecord

  // BEGIN dayGroup
  /** Returns which chat-list group (today, yesterday, last 7 days, older) a timestamp belongs to, using local day boundaries. */
  function dayGroup(when, now) {
    var n = new Date(now);
    var startToday = new Date(n.getFullYear(), n.getMonth(), n.getDate()).getTime();
    var day = 24 * 60 * 60 * 1000;
    var ts = new Date(when).getTime();
    if (ts >= startToday) { return 'groupToday'; }
    if (ts >= startToday - day) { return 'groupYesterday'; }
    if (ts >= startToday - 7 * day) { return 'groupWeek'; }
    return 'groupOlder';
  }
  // END dayGroup

  var PREF_KEY = 'nlq-ui';
  /** Loads the widget's saved display preferences from local storage. */
  function loadPrefs() {
    try { return JSON.parse(window.localStorage.getItem(PREF_KEY) || '{}') || {}; } catch (e) { return {}; }
  }
  /** Saves the widget's display preferences to local storage, ignoring storage errors. */
  function savePrefs() {
    try { window.localStorage.setItem(PREF_KEY, JSON.stringify(prefs)); } catch (e) {  }
  }
  var prefs = loadPrefs();
  currentLanguage = pickLanguage(prefs.lang, navigator.language, LANGUAGES);

  /** Remembers in session storage whether the panel is open. */
  function remember(open) {
    try { window.sessionStorage.setItem('nlq-open', open ? '1' : '0'); } catch (e) {  }
  }
  /** Returns whether the panel was open earlier in this browser tab. */
  function wasOpen() {
    try { return window.sessionStorage.getItem('nlq-open') === '1'; } catch (e) { return false; }
  }

  var host, root, panel, ui = {};
  var dismissedRecord = null;
  var currentQuestion = '';
  var lastView = null;
  var answers = {};
  var busy = false;

  var state = {
    chats: [],
    cursor: null,
    listLoading: false,
    listFailed: false,
    listToken: 0,
    search: '',
    retentionDays: null,
    auditNotice: false,
    quality: { canSave: false, canSuggest: false },
    feedback: { enabled: false, allowSharing: false },
    exampleQuestions: null,
    exampleKey: '',
    createDraft: null,
    currentId: null,
    currentTitle: '',
    lastQuestion: '',
    displayName: null,
    viewToken: 0,
    olderCursor: null,
    loadingOlder: false,
    menuFor: null,
    renaming: null
  };

  /** Calls the service API with the given method, path, body and abort signal and returns the parsed response. */
  // BEGIN crmApiBase
  /** Returns the CRM Web API address for a CRM page address, treating the first path segment as the organization name when the page is not at the site root. */
  function crmApiBase(href, version) {
    var u = new URL(href);
    var segments = u.pathname.split('/').filter(Boolean);
    var org = segments.length > 1 ? '/' + segments[0] : '';
    return u.origin + org + '/api/data/' + version + '/';
  }
  // END crmApiBase

  // BEGIN randomSecret
  /** Makes a random URL-safe secret from 48 random bytes supplied by the given generator. */
  function randomSecret(getRandomValues) {
    var bytes = new Uint8Array(48);
    getRandomValues(bytes);
    var text = '';
    for (var i = 0; i < bytes.length; i++) { text += String.fromCharCode(bytes[i]); }
    return btoa(text).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  }
  // END randomSecret

  // BEGIN tokenFresh
  /** Tells whether a stored session still has at least a minute left. */
  function tokenFresh(entry, now) {
    return !!(entry && entry.token && entry.expiresAt && entry.expiresAt - now > 60000);
  }
  // END tokenFresh

  var AUTH_KEY = 'nlq_session';
  var auth = loadSession();
  var signInPromise = null;
  var signInFailedAt = 0;

  /** Reads a still-valid session from this tab; storage can be unavailable, then there is simply none. */
  function loadSession() {
    try {
      var raw = sessionStorage.getItem(AUTH_KEY);
      var entry = raw ? JSON.parse(raw) : null;
      return tokenFresh(entry, Date.now()) ? entry : null;
    } catch (e) { return null; }
  }

  /** Keeps the session for this tab only, or forgets it when none is given. */
  function saveSession(entry) {
    try {
      if (entry) { sessionStorage.setItem(AUTH_KEY, JSON.stringify(entry)); } else { sessionStorage.removeItem(AUTH_KEY); }
    } catch (e) {  }
  }

  /** Posts JSON to the service without the session, used by the sign-in steps. */
  function postJson(path, body) {
    return fetch(base + path, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
  }

  /** Signs in through the CRM: asks the service for a one-time code, proves it by creating a task as the signed-in CRM user, then exchanges it for a session token; resolves to ok, unavailable (service does not use this sign-in) or failed. */
  function signIn() {
    if (signInPromise) { return signInPromise; }
    signInPromise = (async function () {
      var taskUrl = null;
      try {
        var secret = randomSecret(function (bytes) { return crypto.getRandomValues(bytes); });
        var challenge = await postJson('/api/auth/challenge', { secret: secret });
        if (challenge.status === 404) { return 'unavailable'; }
        if (!challenge.ok) { return 'failed'; }
        var code = await challenge.json();
        var versions = ['v9.0', 'v8.2'];
        for (var i = 0; i < versions.length && !taskUrl; i++) {
          var apiBase = crmApiBase(location.href, versions[i]);
          var made = await fetch(apiBase + 'tasks', {
            method: 'POST', credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json', 'Accept': 'application/json', 'Prefer': 'return=representation' },   // i18n-ok
            body: JSON.stringify({ subject: code.subject })
          });
          if (made.ok) {
            var task = await made.json();
            taskUrl = apiBase + 'tasks(' + task.activityid + ')';   // i18n-ok
          } else if (made.status !== 404) { return 'failed'; }
        }
        if (!taskUrl) { return 'failed'; }
        var verified = await postJson('/api/auth/verify', { nonce: code.nonce, secret: secret });
        if (!verified.ok) { return 'failed'; }
        var data = await verified.json();
        auth = { token: data.token, expiresAt: Date.parse(data.expiresAt) };
        saveSession(auth);
        return 'ok';
      } catch (e) {
        return 'failed';
      } finally {
        if (taskUrl) { fetch(taskUrl, { method: 'DELETE', credentials: 'same-origin' }).catch(function () {  }); }
        signInPromise = null;
      }
    })();
    return signInPromise;
  }

  var handoffTabs = [];

  /** Gives a hand-over code only to a service page this widget opened itself, and only when that page, running on the service origin, asks for it. */
  window.addEventListener('message', function (e) {
    if (!e.data || e.data.type !== 'nlq-ready' || e.origin !== new URL(base).origin) { return; }   // i18n-ok
    var tab = handoffTabs.filter(function (x) { return x === e.source; })[0];
    if (!tab) { return; }
    handoffTabs = handoffTabs.filter(function (x) { return x !== tab; });
    api('POST', '/api/auth/handoff').then(function (res) {
      if (res.ok && res.json && res.json.code) { tab.postMessage({ type: 'nlq-code', code: res.json.code }, new URL(base).origin); }   // i18n-ok
    });
  });

  /** Opens a service page in a new tab; with a session token the page asks this widget for a hand-over code (kept out of the address), otherwise a link works as a normal link and a button simply opens the page. */
  function openWithHandoff(event, path, isLink) {
    if (!tokenFresh(auth, Date.now())) {
      if (!isLink) { window.open(base + path, '_blank', 'noopener'); }
      return;
    }
    event.preventDefault();
    var tab = window.open(base + path, '_blank');
    if (!tab) { return; }
    handoffTabs.push(tab);
    setTimeout(function () { handoffTabs = handoffTabs.filter(function (x) { return x !== tab; }); }, 15000);
  }

  /** Sends one request to the service with the session token when there is one. */
  async function send(method, path, body, signal) {
    var options = { method: method, credentials: 'include', headers: {} };
    if (signal) { options.signal = signal; }
    if (tokenFresh(auth, Date.now())) { options.headers['Authorization'] = 'Bearer ' + auth.token; }   // i18n-ok
    if (body !== undefined) {
      options.headers['Content-Type'] = 'application/json';
      options.body = JSON.stringify(body);
    }
    var res = await fetch(base + path, options);
    var json = null;
    try { json = await res.json(); } catch (e) {  }
    return { ok: res.ok, status: res.status, json: json };
  }

  /** Calls the service; on a 401 it signs in through the CRM once (not more than every 20 seconds after a failure) and repeats the request. */
  async function api(method, path, body, signal) {
    var res = await send(method, path, body, signal);
    if (res.status !== 401 || path.indexOf('/api/auth/') === 0) { return res; }
    if (auth) { auth = null; saveSession(null); }
    if (Date.now() - signInFailedAt < 20000) { return res; }
    var outcome = await signIn();
    if (outcome === 'ok') { return send(method, path, body, signal); }
    if (outcome === 'failed') {
      signInFailedAt = Date.now();
      res.json = { kind: 'failure', message: t('signInFailed') };
    }
    return res;
  }

  /** Shows a short transient message in the widget. */
  function toast(message) {
    var old = ui.panelRoot.querySelector('.toast');
    if (old) { old.remove(); }
    var box = el('div', message, 'toast');
    ui.panelRoot.append(box);
    setTimeout(function () { box.remove(); }, 2600);
  }

  /** Builds the widget's DOM skeleton: launcher button, panel, chat list, message log and input area. */
  function build() {
    host = document.createElement('div');
    var shadow = host.attachShadow({ mode: 'open' });
    var style = document.createElement('style');
    style.textContent = CSS;

    root = el('div', undefined, 'root');
    var fab = el('button', undefined, 'fab');
    fab.title = t('title');
    fab.setAttribute('aria-label', t('title'));
    var fabSvg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    fabSvg.setAttribute('viewBox', '0 0 24 24');
    fabSvg.setAttribute('fill', 'none');
    fabSvg.setAttribute('stroke', '#fff');
    fabSvg.setAttribute('stroke-width', '1.8');
    fabSvg.setAttribute('stroke-linecap', 'round');
    fabSvg.setAttribute('stroke-linejoin', 'round');
    var fabPath = document.createElementNS('http://www.w3.org/2000/svg', 'path');
    fabPath.setAttribute('d', 'M21 12a8 8 0 0 1-11.6 7.1L4 20.5l1.2-4.7A8 8 0 1 1 21 12z');
    fabSvg.append(fabPath);
    fab.append(fabSvg);
    panel = el('div', undefined, 'panel');
    ui.panelRoot = panel;

    var head = el('div', undefined, 'head');
    ui.toggle = el('button', '☰'); ui.toggle.title = t('toggleSidebar');
    ui.title = el('input', undefined, 'title');
    ui.title.maxLength = 60;
    ui.gearBtn = el('button', '⚙'); ui.gearBtn.title = t('settings'); ui.gearBtn.setAttribute('aria-label', t('settings'));
    ui.minBtn = el('button', '–'); ui.minBtn.title = t('minimize');
    ui.maxBtn = el('button', '□'); ui.maxBtn.title = t('maximize');
    var closeBtn = el('button', '×'); closeBtn.title = t('close');
    head.append(ui.toggle, ui.title, ui.gearBtn, ui.minBtn, ui.maxBtn, closeBtn);
    stopKeys(ui.title);

    var grip = el('div', undefined, 'resize');
    grip.title = t('resize');
    var gripL = el('div', undefined, 'resize-l');
    var gripT = el('div', undefined, 'resize-t');

    var body = el('div', undefined, 'body');
    ui.side = el('div', undefined, 'side');
    var newBtn = el('button', '+ ' + t('newChat'), 'newchat');
    ui.search = el('input', undefined, 'search');
    ui.search.placeholder = t('searchChats');
    stopKeys(ui.search);
    ui.list = el('div', undefined, 'list');
    ui.note = el('div', '', 'note');
    ui.side.append(newBtn, ui.search, ui.list, ui.note);

    var main = el('div', undefined, 'main');
    ui.log = el('div', undefined, 'log');
    ui.chip = el('div', undefined, 'chip');
    ui.chip.style.display = 'none';
    var foot = el('div', undefined, 'foot');
    ui.input = el('input', undefined, 'txt');
    ui.input.placeholder = t('placeholder');
    ui.send = el('button', undefined, 'btn send');
    setSendIcon(false);
    ui.menu = el('div', undefined, 'cmdmenu');
    ui.menu.setAttribute('role', 'listbox');
    ui.menu.addEventListener('mousedown', function (e) { e.preventDefault(); });
    ui.input.setAttribute('aria-autocomplete', 'list');
    ui.input.setAttribute('aria-expanded', 'false');
    foot.append(ui.menu, ui.input, ui.send);
    ui.main = main;
    ui.settings = el('div', undefined, 'settings');
    main.append(ui.log, ui.chip, ui.settings, foot);
    body.append(ui.side, main);

    panel.append(head, grip, gripL, gripT, body);
    root.append(fab, panel);
    shadow.append(style, root);

    fab.onclick = function () { openPanel(); };
    closeBtn.onclick = function () { root.classList.remove('open'); remember(false); };
    ui.minBtn.onclick = function () { prefs.state = prefs.state === 'min' ? 'normal' : 'min'; savePrefs(); applyWindow(); };
    ui.maxBtn.onclick = function () { prefs.state = prefs.state === 'max' ? 'normal' : 'max'; savePrefs(); applyWindow(); };
    ui.toggle.onclick = function () { prefs.side = isSideClosed() ? 'open' : 'closed'; savePrefs(); applyWindow(); };
    ui.title.addEventListener('change', commitTitle);
    ui.title.addEventListener('keydown', function (e) { if (e.key === 'Enter') { ui.title.blur(); } });

    newBtn.onclick = function () { newChat(); };
    ui.gearBtn.onclick = function (e) { e.stopPropagation(); if (state.settingsOpen) { closeSettings(); } else { openSettings(); } };
    var timer = null;
    ui.search.addEventListener('input', function () {
      clearTimeout(timer);
      timer = setTimeout(function () { state.search = ui.search.value.trim(); loadChats(true); }, 300);
    });
    ui.list.addEventListener('scroll', function () {
      if (ui.list.scrollTop + ui.list.clientHeight >= ui.list.scrollHeight - 60) { loadChats(false); }
    });
    root.addEventListener('click', function () { closeMenus(); });

    ui.log.addEventListener('scroll', function () {
      if (ui.log.scrollTop < 40) { loadOlder(); }
    });

    ui.send.onclick = ask;
    ui.input.addEventListener('keydown', function (e) {
      e.stopPropagation();
      if (e.isComposing) { return; }
      if (menu.open) {
        if (e.key === 'ArrowDown' || e.key === 'ArrowUp') { e.preventDefault(); moveMenu(e.key === 'ArrowDown' ? 1 : -1); return; }
        if (e.key === 'Escape') { closeMenu(); return; }
        if (e.key === 'Tab') {
          if (menu.items.length) { e.preventDefault(); ui.input.value = '/' + menu.items[menu.index].name; menu.index = 0; refreshMenu(); }
          return;
        }
        if (e.key === 'Enter') {
          e.preventDefault();
          if (menu.items.length) { runCommand(menu.items[menu.index]); }
          return;
        }
      }
      if (e.key === 'Enter') { ask(); }
    });
    ui.input.addEventListener('input', function () { menu.index = 0; refreshMenu(); });
    ui.input.addEventListener('blur', closeMenu);
    ui.input.addEventListener('keyup', function (e) { e.stopPropagation(); });
    ui.input.addEventListener('keypress', function (e) { e.stopPropagation(); });

    setupResize(grip, true, true);
    setupResize(gripL, true, false);
    setupResize(gripT, false, true);
    applyTheme();
    if (window.ResizeObserver) {
      new ResizeObserver(function () { root.classList.toggle('narrow', panel.clientWidth < 720); }).observe(panel);
    }

    applyWindow();
    newChat();
    loadChats(true);
    loadProfile();
    if (wasOpen()) { root.classList.add('open'); updateChip(); }
  }

  /** Opens the chat panel and focuses the input. */
  function openPanel() {
    root.classList.add('open');
    remember(true);
    if (state.currentId !== null || ui.log.children.length === 0) { newChat(); }
    loadChats(true);
    updateChip();
    ui.input.focus();
  }

  /** Attaches the widget to the CRM page and wires up its startup behaviour. */
  function mount() {
    if (!host) { build(); }
    if (!host.isConnected && document.body) { document.body.appendChild(host); }
  }

  /** Returns whether the side chat list is currently collapsed. */
  function isSideClosed() {
    if (prefs.side === 'closed') { return true; }
    if (prefs.side === 'open') { return false; }
    return root.classList.contains('narrow');
  }

  /** Applies the saved window state (size, maximized, side list) to the panel. */
  function applyWindow() {
    var s = prefs.state || 'normal';
    root.classList.toggle('max', s === 'max');
    panel.classList.toggle('min', s === 'min');
    ui.maxBtn.title = s === 'max' ? t('restore') : t('maximize');
    ui.minBtn.title = s === 'min' ? t('restore') : t('minimize');
    if (prefs.w && prefs.h && s === 'normal') {
      panel.style.width = prefs.w + 'px';
      panel.style.height = prefs.h + 'px';
    } else if (s !== 'normal') {
      panel.style.width = '';
      panel.style.height = '';
    }
    ui.side.classList.toggle('closed', isSideClosed());
  }

  /** Makes a panel edge or corner draggable so the user can resize the window. */
  function setupResize(grip, horizontal, vertical) {
    grip.addEventListener('pointerdown', function (e) {
      e.preventDefault();
      var startX = e.clientX, startY = e.clientY;
      var startW = panel.offsetWidth, startH = panel.offsetHeight;
      grip.setPointerCapture(e.pointerId);
      /** Updates the panel size while the resize handle is being dragged. */
      function move(ev) {
        if (horizontal) { panel.style.width = Math.max(420, Math.min(window.innerWidth - 24, startW + (startX - ev.clientX))) + 'px'; }
        if (vertical) { panel.style.height = Math.max(360, Math.min(window.innerHeight - 24, startH + (startY - ev.clientY))) + 'px'; }
      }
      /** Ends a resize drag and removes the pointer listeners. */
      function up() {
        grip.removeEventListener('pointermove', move);
        grip.removeEventListener('pointerup', up);
        prefs.w = panel.offsetWidth;
        prefs.h = panel.offsetHeight;
        savePrefs();
      }
      grip.addEventListener('pointermove', move);
      grip.addEventListener('pointerup', up);
    });
  }

  var systemDark = window.matchMedia ? window.matchMedia('(prefers-color-scheme: dark)') : null;
  /** Applies the chosen light, dark or automatic theme to the widget. */
  function applyTheme() {
    if (!root) { return; }
    var mode = prefs.theme === 'dark' || prefs.theme === 'auto' ? prefs.theme : 'light';
    var dark = mode === 'dark' || (mode === 'auto' && !!systemDark && systemDark.matches);
    root.classList.toggle('dark', dark);
    root.classList.toggle('fs-sm', prefs.fs === 'sm');
    root.classList.toggle('fs-lg', prefs.fs === 'lg');
    root.classList.toggle('compact', prefs.density === 'compact');
  }
  if (systemDark && systemDark.addEventListener) { systemDark.addEventListener('change', function () { applyTheme(); }); }

  /** Closes any open chat or settings dropdown menus. */
  function closeMenus() {
    if (state.menuFor === null && !ui.settingsMenu) { return; }
    state.menuFor = null;
    if (ui.settingsMenu) { ui.settingsMenu.remove(); ui.settingsMenu = null; }
    renderList();
  }

  /** Renders the note shown above the chat list, such as the retention notice. */
  function renderNote() {
    var parts = [];
    if (state.retentionDays !== null) { parts.push(state.retentionDays > 0 ? t('retentionDays', { n: state.retentionDays }) : t('retentionNone')); }
    if (state.auditNotice) { parts.push(t('auditNotice')); }
    ui.note.textContent = parts.join(' ');
  }

  /** Creates placeholder skeleton rows while the chat list loads. */
  function skeletonRows(n) {
    var frag = document.createDocumentFragment();
    for (var i = 0; i < n; i++) { var s = el('div', undefined, 'sk'); s.style.width = (60 + (i * 13) % 35) + '%'; frag.append(s); }
    return frag;
  }

  /** Renders the grouped, pinned-first chat list in the side panel. */
  function renderList() {
    ui.list.replaceChildren();
    if (state.listLoading && state.chats.length === 0) { ui.list.append(skeletonRows(5)); return; }
    if (state.listFailed && state.chats.length === 0) {
      var fail = el('div', undefined, 'muted'); fail.style.padding = '8px';
      fail.append(el('div', t('loadFailed'), 'error'));
      var retry = el('button', t('retry'), 'btn'); retry.onclick = function () { loadChats(true); };
      fail.append(retry);
      ui.list.append(fail);
      return;
    }
    if (state.chats.length === 0) {
      var none = el('div', state.search ? t('noMatch') : t('noChats'), 'muted'); none.style.padding = '8px';
      ui.list.append(none);
      return;
    }
    var now = Date.now();
    var last = null;
    state.chats.forEach(function (chat) {
      var group = chat.pinned ? 'groupPinned' : dayGroup(chat.updatedAt, now);
      if (group !== last) { ui.list.append(el('div', t(group), 'group')); last = group; }
      ui.list.append(chatRow(chat));
    });
    if (state.listLoading) { ui.list.append(skeletonRows(2)); }
    if (state.listFailed) {
      var more = el('button', t('retry'), 'btn'); more.style.margin = '6px';
      more.onclick = function () { loadChats(false); };
      ui.list.append(more);
    }
  }

  /** Builds one chat list row with its title, pin marker and menu button. */
  function chatRow(chat) {
    var row = el('div', undefined, 'item' + (chat.id === state.currentId ? ' active' : '') + (state.menuFor === chat.id ? ' menu-open' : ''));
    if (state.renaming === chat.id) {
      var input = el('input', undefined, 'rename');
      input.value = chat.title; input.maxLength = 60;
      stopKeys(input);
      var done = false;
      var finish = function (save) {
        if (done) { return; }
        done = true;
        state.renaming = null;
        if (save && input.value.trim() && input.value.trim() !== chat.title) { renameChat(chat, input.value); }
        else { renderList(); }
      };
      input.addEventListener('keydown', function (e) { if (e.key === 'Enter') { finish(true); } else if (e.key === 'Escape') { finish(false); } });
      input.addEventListener('blur', function () { finish(true); });
      input.addEventListener('click', function (e) { e.stopPropagation(); });
      row.append(input);
      setTimeout(function () { input.focus(); input.select(); }, 0);
      return row;
    }
    row.append(el('span', chat.title, 't'));
    var dots = el('button', '⋯', 'dots'); dots.title = t('options');
    dots.onclick = function (e) { e.stopPropagation(); state.menuFor = state.menuFor === chat.id ? null : chat.id; renderList(); };
    row.append(dots);
    row.onclick = function () { state.menuFor = null; openChat(chat); if (root.classList.contains('narrow')) { prefs.side = 'closed'; applyWindow(); } };
    if (state.menuFor === chat.id) { row.append(chatMenu(chat)); }
    return row;
  }

  /** Builds the per-chat menu with rename, pin and delete actions. */
  function chatMenu(chat) {
    var menu = el('div', undefined, 'menu');
    menu.addEventListener('click', function (e) { e.stopPropagation(); });
    var rename = el('button', t('menuRename'));
    rename.onclick = function () { state.menuFor = null; state.renaming = chat.id; renderList(); };
    var pin = el('button', chat.pinned ? t('menuUnpin') : t('menuPin'));
    pin.onclick = function () { state.menuFor = null; pinChat(chat, !chat.pinned); };
    var del = el('button', t('menuDelete'), 'danger');
    del.onclick = function () { state.menuFor = null; renderList(); deleteChat(chat); };
    menu.append(rename, pin, del);
    return menu;
  }

  /** Opens the in-window settings page on the given section. */
  function openSettings(section) {
    closeMenus();
    state.settingsOpen = true;
    if (section) { state.settingsSection = section; }
    ui.main.classList.add('settingsmode');
    ui.gearBtn.classList.add('active');
    ui.title.value = t('settings');
    ui.title.readOnly = true;
    drawSettings();
  }

  /** Closes the settings page and returns to the chat. */
  function closeSettings() {
    state.settingsOpen = false;
    ui.main.classList.remove('settingsmode');
    ui.gearBtn.classList.remove('active');
    if (state.currentId !== null) { ui.title.value = state.currentTitle; ui.title.readOnly = false; }
    else { ui.title.value = t('newChatTitle'); ui.title.readOnly = true; }
    if (ui.input) { ui.input.focus(); }
  }

  /** Builds a segmented choice row that writes the selected value to the given preference. */
  function settingSegment(label, key, fallback, options) {
    var box = el('div', undefined, 'sset');
    box.append(el('span', label, 'slabel'));
    var seg = el('div', undefined, 'seg');
    options.forEach(function (pair) {
      var b = el('button', pair[1], (prefs[key] === undefined ? fallback : prefs[key]) === pair[0] ? 'on' : '');
      b.onclick = function () { prefs[key] = pair[0]; savePrefs(); applyTheme(); drawSettings(); };
      seg.append(b);
    });
    box.append(seg);
    return box;
  }

  /** Builds an on/off toggle row bound to the given preference. */
  function settingToggle(label, key, fallback) {
    var row = el('label', undefined, 'strow');
    var box = el('input');
    box.type = 'checkbox';
    box.checked = prefs[key] === undefined ? fallback : !!prefs[key];
    box.onchange = function () { prefs[key] = box.checked; savePrefs(); };
    row.append(box, el('span', label));
    return row;
  }

  /** Redraws the settings page with its section tabs and the active pane. */
  function drawSettings() {
    var view = ui.settings;
    view.replaceChildren();
    var section = state.settingsSection || 'appearance';
    var nav = el('div', undefined, 'snav');
    var back = el('button', t('setBack'), 'btn');
    back.onclick = closeSettings;
    nav.append(back);
    [['appearance', t('setSecAppearance')], ['chat', t('setSecChat')], ['profile', t('setSecProfile')], ['privacy', t('setSecPrivacy')], ['help', t('setSecHelp')]].forEach(function (pair) {
      var b = el('button', pair[1], 'snavbtn' + (pair[0] === section ? ' on' : ''));
      b.onclick = function () { state.settingsSection = pair[0]; drawSettings(); };
      nav.append(b);
    });
    var pane = el('div', undefined, 'spane');
    var title = { appearance: t('setSecAppearance'), chat: t('setSecChat'), profile: t('setSecProfile'), privacy: t('setSecPrivacy'), help: t('setSecHelp') }[section];
    pane.append(el('h3', title));
    if (section === 'appearance') { paneAppearance(pane); }
    else if (section === 'chat') { paneChat(pane); }
    else if (section === 'profile') { paneProfile(pane); }
    else if (section === 'privacy') { panePrivacy(pane); }
    else { paneHelp(pane); }
    view.append(nav, pane);
  }

  /** Fills the Appearance settings pane (theme and display options). */
  function paneAppearance(pane) {
    pane.append(settingSegment(t('setTheme'), 'theme', 'light', [['light', t('themeLight')], ['dark', t('themeDark')], ['auto', t('themeAuto')]]));
    pane.append(settingSegment(t('setTextSize'), 'fs', 'md', [['sm', t('sizeSmall')], ['md', t('sizeNormal')], ['lg', t('sizeLarge')]]));
    pane.append(settingSegment(t('setDensity'), 'density', 'comfortable', [['comfortable', t('densityComfortable')], ['compact', t('densityCompact')]]));
    var reset = el('button', t('setResetWindow'), 'btn');
    reset.onclick = function () {
      delete prefs.w;
      delete prefs.h;
      savePrefs();
      panel.style.width = '';
      panel.style.height = '';
      toast(t('setResetDone'));
    };
    var box = el('div', undefined, 'sset');
    box.append(reset);
    pane.append(box);
  }

  /** Builds the Chat pane of the widget settings, adding toggles and segmented choices for preview rows, explanation, suggestions and similar chat options. */
  function paneChat(pane) {
    pane.append(settingSegment(t('setPreviewRows'), 'rows', 5, [[5, '5'], [10, '10'], [20, '20']]));
    pane.append(settingToggle(t('setExplainOpen'), 'explainOpen', false));
    pane.append(settingToggle(t('setSuggestions'), 'suggestions', true));
    pane.append(el('div', t('setChatNote'), 'muted'));
  }

  /** Fills the Profile settings pane with the address name and language choice. */
  function paneProfile(pane) {
    var name = el('div', undefined, 'sset');
    name.append(el('span', t('nameTitle'), 'slabel'));
    var line = el('div', undefined, 'sline');
    var input = el('input', undefined, 'txt');
    input.maxLength = 30;
    input.placeholder = t('namePlaceholder');
    input.value = state.displayName || '';
    stopKeys(input);
    var save = el('button', t('save'), 'btn primary');
    var submit = async function () {
      save.disabled = true;
      try {
        var r = await api('PUT', '/api/me/preferences', { displayName: input.value });
        if (!r.ok) { throw new Error('status ' + r.status); }
        state.displayName = (r.json && r.json.displayName) || null;
        toast(t('setNameSaved'));
        if (state.currentId === null && ui.log.querySelector('.empty')) { showEmpty(); }
      } catch (e) { toast(t('nameFailed')); }
      save.disabled = false;
    };
    save.onclick = submit;
    input.addEventListener('keydown', function (e) { if (e.key === 'Enter') { submit(); } });
    line.append(input, save);
    name.append(line, el('div', t('nameHint'), 'small'));
    pane.append(name);

    var lang = el('div', undefined, 'sset');
    lang.append(el('span', t('language'), 'slabel'));
    var seg = el('div', undefined, 'seg');
    LANGUAGES.forEach(function (code) {
      var b = el('button', LANGUAGE_NAMES[code], code === currentLanguage ? 'on' : '');
      b.onclick = function () { chooseLanguage(code); };
      seg.append(b);
    });
    lang.append(seg);
    pane.append(lang);
  }

  /** Converts the visible chat to plain text, skipping buttons and charts and separating table cells with tabs. */
  function plainText(node) {
    if (node.nodeType === 3) { return node.textContent; }
    if (node.nodeType !== 1 || /^(BUTTON|INPUT|CANVAS|STYLE|SCRIPT|SVG)$/i.test(node.tagName)) { return ''; }
    if (node.tagName === 'TR') {
      return Array.prototype.map.call(node.children, function (c) { return c.textContent.trim(); }).join('\t') + '\n';
    }
    var out = '';
    node.childNodes.forEach(function (c) { out += plainText(c); });
    return /^(DIV|P|DETAILS|SUMMARY|TABLE)$/.test(node.tagName) ? out + '\n' : out;
  }

  /** Downloads the visible chat as a plain text file. */
  function downloadChat() {
    if (!ui.log.querySelector('.msg')) { toast(t('setDownloadNone')); return; }
    var lines = [];
    Array.prototype.forEach.call(ui.log.querySelectorAll('.msg'), function (m) {
      var you = m.querySelector('.you');
      var bot = m.querySelector('.bot');
      if (you) { lines.push(t('setDlYou') + ': ' + you.textContent.trim()); }
      else if (bot) { lines.push(t('setDlBot') + ': ' + plainText(bot).replace(/\n{3,}/g, '\n\n').trim()); }
      lines.push('');
    });
    var blob = new Blob(['\ufeff' + lines.join('\n')], { type: 'text/plain;charset=utf-8' });
    var url = URL.createObjectURL(blob);
    var a = document.createElement('a');
    a.href = url;
    a.download = 'chat-' + new Date().toISOString().slice(0, 10) + '.txt';
    ui.panelRoot.append(a);
    a.click();
    a.remove();
    setTimeout(function () { URL.revokeObjectURL(url); }, 4000);
  }

  /** Fills the Privacy settings pane, including chat download and history deletion. */
  function panePrivacy(pane) {
    var box = el('div', undefined, 'sset');
    box.append(el('span', t('setDataTitle'), 'slabel'));
    var notes = [];
    if (state.retentionDays !== null) { notes.push(state.retentionDays > 0 ? t('retentionDays', { n: state.retentionDays }) : t('retentionNone')); }
    if (state.auditNotice) { notes.push(t('auditNotice')); }
    notes.push(t('setPrivacyNote'));
    notes.forEach(function (n) { box.append(el('div', n, 'small')); });
    pane.append(box);
    var actions = el('div', undefined, 'sset');
    var download = el('button', t('setDownload'), 'btn');
    download.onclick = downloadChat;
    var del = el('button', t('deleteAllMenu'), 'btn danger');
    del.style.marginLeft = '8px';
    del.onclick = function () { deleteAllChats(); };
    actions.append(download, del);
    pane.append(actions);
  }

  /** Builds the Help pane of the widget settings, listing the keyboard shortcuts and help text shown to the user. */
  function paneHelp(pane) {
    var keys = el('div', undefined, 'sset');
    keys.append(el('span', t('setShortcuts'), 'slabel'));
    var list = el('ul', undefined, 'shortcuts');
    [t('shortcutSend'), t('shortcutCmd'), t('shortcutTab'), t('shortcutEsc')].forEach(function (x) { list.append(el('li', x)); });
    keys.append(list);
    pane.append(keys);
    var cmds = el('div', undefined, 'sset');
    cmds.append(el('span', t('setCommands'), 'slabel'));
    COMMANDS.forEach(function (c) {
      var row = el('div', undefined, 'cmdrow');
      row.append(el('span', '/' + c.name, 'cmdname'), el('span', t(c.descKey), 'muted'));
      cmds.append(row);
    });
    pane.append(cmds);
    var adminRow = el('div', undefined, 'sset');
    var adminBtn = el('button', t('openAdmin'), 'btn');
    adminBtn.onclick = function (e) { openWithHandoff(e, '/admin-next/', false); };   // i18n-ok
    adminRow.append(adminBtn);
    pane.append(adminRow);
    var ex = el('div', undefined, 'sset');
    ex.append(el('span', t('helpExamples'), 'slabel'));
    var chips = el('div', undefined, 'chips');
    chips.style.justifyContent = 'flex-start';
    (state.exampleQuestions || tv('examples')).forEach(function (text) {
      var b = el('button', text, 'chipbtn');
      b.onclick = function () { closeSettings(); ui.input.value = text; ui.input.focus(); };
      chips.append(b);
    });
    ex.append(chips);
    pane.append(ex);
  }

  /** Saves the language choice to the user's account on the server, then rebuilds the UI in that language. */
  async function chooseLanguage(code) {
    if (code === currentLanguage) { return; }
    if (busy) { toast(t('languageWait')); return; }
    try {
      var r = await api('PUT', '/api/me/preferences', { language: code });
      if (!r.ok) { throw new Error('status ' + r.status); }
      applyLanguage((r.json && r.json.effectiveLanguage) || code);
    } catch (e) { toast(t('languageFailed')); }
  }

  /** Switches the widget language by rebuilding the UI while keeping the open chat and panel state; not done while an answer is pending. */
  function applyLanguage(code) {
    var next = LANGUAGES.indexOf(code) >= 0 ? code : 'en';
    if (next === currentLanguage || busy) { return; }
    currentLanguage = next;
    prefs.lang = next;
    savePrefs();
    if (!host) { return; }
    var keepOpen = root.classList.contains('open');
    var wasSettings = !!state.settingsOpen;
    var reopen = state.currentId !== null ? { id: state.currentId, title: state.currentTitle } : null;
    host.remove();
    host = null;
    ui.settingsMenu = null;
    state.exampleKey = '';
    build();
    mount();
    if (keepOpen) { root.classList.add('open'); updateChip(); }
    if (reopen) { openChat(reopen); }
    if (wasSettings) { openSettings(); }
  }

  /** Replaces the built-in example questions with the server-configured ones and redraws the empty chat if they changed. */
  function applyExamples(list) {
    var clean = Array.isArray(list) ? list.filter(function (x) { return typeof x === 'string' && x.length > 0; }) : [];
    var key = JSON.stringify(clean);
    if (key === state.exampleKey) { return; }
    state.exampleKey = key;
    state.exampleQuestions = clean.length > 0 ? clean : null;
    ui.input.placeholder = state.exampleQuestions ? t('placeholderExample', { text: state.exampleQuestions[0] }) : t('placeholder');
    if (state.currentId === null && ui.log.querySelector('.empty')) { showEmpty(); }
  }

  /** Loads a page of the chat list, either from the start or the next page, ignoring stale responses. */
  async function loadChats(reset) {
    if (!reset && (state.listLoading || state.cursor === null)) { return; }
    var token = ++state.listToken;
    state.listLoading = true;
    state.listFailed = false;
    if (reset) { state.cursor = null; }
    renderList();
    var path = '/api/chats?take=30' + (state.cursor ? '&cursor=' + encodeURIComponent(state.cursor) : '') + (state.search ? '&q=' + encodeURIComponent(state.search) : '');
    try {
      var r = await api('GET', path);
      if (token !== state.listToken) { return; }
      if (!r.ok) { throw new Error('status ' + r.status); }
      state.chats = reset ? r.json.items : state.chats.concat(r.json.items);
      state.cursor = r.json.nextCursor || null;
      state.retentionDays = typeof r.json.retentionDays === 'number' ? r.json.retentionDays : state.retentionDays;
      state.auditNotice = r.json.auditNotice === true;
      state.feedback = { enabled: !!(r.json.feedback && r.json.feedback.enabled), allowSharing: !!(r.json.feedback && r.json.feedback.allowSharing) };
      state.quality = { canSave: !!(r.json.quality && r.json.quality.canSave), canSuggest: !!(r.json.quality && r.json.quality.canSuggest) };
      applyExamples(r.json.exampleQuestions);
    } catch (e) {
      if (token !== state.listToken) { return; }
      state.listFailed = true;
    }
    state.listLoading = false;
    renderNote();
    renderList();
  }

  /** Renames a chat on the server and updates the list. */
  async function renameChat(chat, title) {
    try {
      var r = await api('PATCH', '/api/chats/' + chat.id, { title: title });
      if (!r.ok) { throw new Error('status ' + r.status); }
      if (state.currentId === chat.id) { state.currentTitle = title.trim(); ui.title.value = state.currentTitle; }
    } catch (e) { toast(t('renameFailed')); }
    loadChats(true);
  }

  /** Pins or unpins a chat on the server and updates the list. */
  async function pinChat(chat, pinned) {
    try {
      var r = await api('PATCH', '/api/chats/' + chat.id, { pinned: pinned });
      if (!r.ok) { throw new Error('status ' + r.status); }
    } catch (e) { toast(t('pinFailed')); }
    loadChats(true);
  }

  /** Commits the edited chat title from the inline editor. */
  function commitTitle() {
    if (state.currentId === null) { return; }
    var value = ui.title.value.trim();
    if (!value || value === state.currentTitle) { ui.title.value = state.currentTitle; return; }
    renameChat({ id: state.currentId, title: state.currentTitle }, value);
  }

  /** Shows an in-widget confirmation dialog instead of the browser's confirm window. */
  function confirmDialog(options) {
    return new Promise(function (resolve) {
      var layer = el('div', undefined, 'layer');
      var box = el('div', undefined, 'dialog');
      box.append(el('h3', options.title));
      var paragraphs = options.lines.map(function (line) { var p = el('p', line); box.append(p); return p; });
      var check = null;
      if (options.checkbox) {
        var label = el('label');
        check = el('input'); check.type = 'checkbox';
        label.append(check, el('span', options.checkbox));
        box.append(label);
        if (options.checkedBody) {
          check.addEventListener('change', function () { paragraphs[0].textContent = check.checked ? options.checkedBody : options.lines[0]; });
        }
      }
      if (options.small) { box.append(el('p', options.small, 'small')); }
      var row = el('div', undefined, 'row');
      var cancel = el('button', t('cancel'), 'btn');
      var ok = el('button', options.confirmLabel, 'btn danger');
      row.append(cancel, ok);
      box.append(row);
      layer.append(box);
      var close = function (confirmed) { layer.remove(); resolve({ confirmed: confirmed, checked: !!(check && check.checked) }); };
      cancel.onclick = function () { close(false); };
      ok.onclick = function () { close(true); };
      layer.addEventListener('click', function (e) { if (e.target === layer) { close(false); } });
      layer.addEventListener('keydown', function (e) { e.stopPropagation(); if (e.key === 'Escape') { close(false); } });
      ui.panelRoot.append(layer);
      cancel.focus();
    });
  }

  /** Loads the user's address name, which is private to them, shown in the greeting and never sent to the model. */
  async function loadProfile() {
    try {
      var r = await api('GET', '/api/me/preferences');
      if (r.ok && r.json) {
        state.displayName = r.json.displayName || null;
        if (r.json.effectiveLanguage) { applyLanguage(r.json.effectiveLanguage); }
        if (state.currentId === null && ui.log.querySelector('.empty')) { showEmpty(); }
      }
    } catch (e) {  }
  }

  /** Lets the user edit how they are addressed in the greeting. */
  function editName() {
    var layer = el('div', undefined, 'layer');
    var box = el('div', undefined, 'dialog');
    box.append(el('h3', t('nameTitle')));
    var input = el('input', undefined, 'txt');
    input.style.width = '100%';
    input.maxLength = 30;
    input.placeholder = t('namePlaceholder');
    input.value = state.displayName || '';
    stopKeys(input);
    box.append(input, el('p', t('nameHint'), 'small'));
    var row = el('div', undefined, 'row');
    var cancel = el('button', t('cancel'), 'btn');
    var save = el('button', t('save'), 'btn primary');
    row.append(cancel, save);
    box.append(row);
    layer.append(box);
    var close = function () { layer.remove(); };
    var submit = async function () {
      save.disabled = true;
      try {
        var r = await api('PUT', '/api/me/preferences', { displayName: input.value });
        if (!r.ok) { throw new Error('status ' + r.status); }
        state.displayName = (r.json && r.json.displayName) || null;
        close();
        if (state.currentId === null && ui.log.querySelector('.empty')) { showEmpty(); }
      } catch (e) { save.disabled = false; toast(t('nameFailed')); }
    };
    cancel.onclick = close;
    save.onclick = submit;
    input.addEventListener('keydown', function (e) { if (e.key === 'Enter') { submit(); } });
    layer.addEventListener('click', function (e) { if (e.target === layer) { close(); } });
    layer.addEventListener('keydown', function (e) { e.stopPropagation(); if (e.key === 'Escape') { close(); } });
    ui.panelRoot.append(layer);
    input.focus();
    input.select();
  }

  /** Deletes a chat after confirmation and refreshes the list. */
  async function deleteChat(chat) {
    var lines = [t('deleteChatBody', { name: chat.title })];
    if (chat.pinned) { lines.push(t('deleteChatPinned')); }
    if (state.feedback.allowSharing) { lines.push(t('fbDeleteNote')); }
    var answer = await confirmDialog({ title: t('deleteChatTitle'), lines: lines, small: t('crmNote'), confirmLabel: t('del') });
    if (!answer.confirmed) { return; }
    toast(t('deleting'));
    try {
      var r = await api('DELETE', '/api/chats/' + chat.id);
      if (!r.ok && r.status !== 404) { throw new Error('status ' + r.status); }
      if (state.currentId === chat.id) { newChat(); }
    } catch (e) { toast(t('deleteFailed')); }
    loadChats(true);
  }

  /** Deletes all chats after confirmation, with an option to include pinned ones. */
  async function deleteAllChats() {
    var counts;
    try {
      var r = await api('GET', '/api/chats/counts');
      if (!r.ok) { throw new Error('status ' + r.status); }
      counts = r.json;
    } catch (e) { toast(t('loadFailed')); return; }
    if (!counts.total) { toast(t('deleteAllNothing')); return; }
    var others = counts.total - counts.pinned;
    var body = counts.pinned > 0
      ? t('deleteAllBody', { n: others, p: counts.pinned })
      : t('deleteAllBodyNoPinned', { n: counts.total });
    var answer = await confirmDialog({
      title: t('deleteAllTitle'),
      lines: [body],
      checkbox: counts.pinned > 0 ? t('deleteAllIncludePinned', { p: counts.pinned }) : null,
      checkedBody: t('deleteAllBodyWithPinned', { n: counts.total, p: counts.pinned }),
      small: t('crmNoteAll'),
      confirmLabel: t('deleteAll')
    });
    if (!answer.confirmed) { return; }
    toast(t('deleting'));
    try {
      var d = await api('DELETE', '/api/chats?includePinned=' + (answer.checked ? 'true' : 'false'));
      if (!d.ok) { throw new Error('status ' + d.status); }
      if (state.currentId !== null) { newChat(); }
    } catch (e) { toast(t('deleteFailed')); }
    loadChats(true);
  }

  /** Creates a message container element of the given kind in the chat log. */
  function makeMessage(cls) {
    var m = el('div', undefined, 'msg');
    var inner = el('div', undefined, cls);
    m.append(inner);
    return { wrap: m, inner: inner };
  }

  /** Appends a new message bubble to the chat log and scrolls to it. */
  function addMessage(cls) {
    var made = makeMessage(cls);
    ui.log.append(made.wrap);
    return made.inner;
  }

  /** Scrolls the message log to the bottom. */
  function scrollDown() { ui.log.scrollTop = ui.log.scrollHeight; }

  /** Sets the send button icon to Send or Stop using a fixed SVG, with the label in title and aria-label. */
  function setSendIcon(stopping) {
    var svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svg.setAttribute('viewBox', '0 0 24 24');
    svg.setAttribute('width', '19');
    svg.setAttribute('height', '19');
    svg.setAttribute('fill', 'none');
    svg.setAttribute('stroke', 'currentColor');
    svg.setAttribute('stroke-width', '1.9');
    svg.setAttribute('stroke-linecap', 'round');
    svg.setAttribute('stroke-linejoin', 'round');
    svg.setAttribute('aria-hidden', 'true');
    var shape;
    if (stopping) {
      shape = document.createElementNS('http://www.w3.org/2000/svg', 'rect');
      shape.setAttribute('x', '6.5');
      shape.setAttribute('y', '6.5');
      shape.setAttribute('width', '11');
      shape.setAttribute('height', '11');
      shape.setAttribute('rx', '2');
      shape.setAttribute('fill', 'currentColor');
    } else {
      shape = document.createElementNS('http://www.w3.org/2000/svg', 'path');
      shape.setAttribute('d', 'M21.5 3.5L10.5 14.5M21.5 3.5l-7 18-4-7-7-4z');
    }
    svg.append(shape);
    ui.send.replaceChildren(svg);
    var label = stopping ? t('stop') : t('ask');
    ui.send.title = label;
    ui.send.setAttribute('aria-label', label);
  }

  /** Switches the input between idle and busy so the Send button becomes a clickable Stop while a question runs. */
  function setBusy(value) {
    busy = value;
    setSendIcon(value);
    ui.send.classList.toggle('danger', value);
    ui.send.onclick = value ? stop : ask;
    if (value) { closeMenu(); }
  }

// BEGIN chartKit
var chartKit = (function () {
  var PALETTE = ['#0b5cad', '#d9822b', '#2a9d8f', '#8e5ea2', '#b00020', '#6b7280'];
  var DASHES = [[], [6, 4], [2, 3], [10, 3, 2, 3], [], [4, 4]];

  /** Folds text to lower case without diacritics for matching. */
  function fold(s) { return String(s || '').toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/ı/g, 'i'); }

  /** Checks whether the question asks for a chart, otherwise the table is shown first. */
  function wants(question) {
    var f = fold(question);
    return /(grafi[gk]|cizelge|trend|dagilim|chart|graph|plot|distribution|over time|zaman icinde)/.test(f);
  }

  /** Computes rounded axis tick values for a value range. */
  function niceTicks(min, max) {
    if (!(max > min)) { max = min + 1; }
    var raw = (max - min) / 4;
    var pow = Math.pow(10, Math.floor(Math.log10(raw)));
    var f = raw / pow;
    var step = (f <= 1 ? 1 : f <= 2 ? 2 : f <= 5 ? 5 : 10) * pow;
    var lo = Math.floor(min / step) * step;
    var hi = Math.ceil(max / step) * step;
    var ticks = [];
    for (var v = lo; v <= hi + step / 2; v += step) { ticks.push(Math.abs(v) < step / 1e6 ? 0 : v); }
    return { min: lo, max: hi, ticks: ticks };
  }

  /** Formats an x-axis label for the chart category kind and locale. */
  function xLabel(xKind, raw, locale) {
    try {
      if (xKind === 'month') { return new Date(2000, +raw - 1, 1).toLocaleDateString(locale, { month: 'short' }); }
      if (xKind === 'quarter') { return 'Q' + raw; }
      if (xKind === 'week') { return 'W' + raw; }
      if (xKind === 'yearmonth') { var p = raw.split('-'); return new Date(+p[0], +p[1] - 1, 1).toLocaleDateString(locale, { month: 'short', year: '2-digit' }); }
      if (xKind === 'yearquarter') { var q = raw.split('-'); return 'Q' + q[1] + ' ' + q[0]; }
    } catch (e) {  }
    return raw;
  }

  /** Builds the drawable chart model from the server chart data using the caller-supplied translated texts. */
  function model(chart, locale, texts) {
    var n = chart.labels.length;
    var labels = chart.labels.map(function (raw, i) {
      if (chart.xKind === 'category') {
        if (chart.hasOther && i === n - 1) { return texts.other; }
        return raw === '(empty)' ? texts.empty : raw;
      }
      return xLabel(chart.xKind, raw, locale);
    });
    var digits = chart.fn === 'count' || chart.fn === 'countcolumn' ? 0 : 2;
    var nf = new Intl.NumberFormat(locale, { maximumFractionDigits: digits });
    var series = chart.series.map(function (s, i) {
      return { name: s.name, values: s.values, color: PALETTE[i % PALETTE.length], dash: DASHES[i % DASHES.length] };
    });
    var min = 0, max = 0, hi = null, lo = null;
    series.forEach(function (s) {
      s.values.forEach(function (v, i) {
        if (v === null) { return; }
        if (v < min) { min = v; }
        if (v > max) { max = v; }
        if (hi === null || v > hi.v) { hi = { v: v, label: labels[i] }; }
        if (lo === null || v < lo.v) { lo = { v: v, label: labels[i] }; }
      });
    });
    var axis = niceTicks(min, max);
    var kindText = chart.kind === 'line' ? texts.line : texts.bar;
    var summary = kindText + ': ' + chart.measure + ', ' + n + ' ' + (hi ? '(' + hi.label + ' ' + nf.format(hi.v) + ' / ' + lo.label + ' ' + nf.format(lo.v) + ')' : '');
    return {
      kind: chart.kind, labels: labels, series: series, axis: axis, format: function (v) { return nf.format(v); },
      measure: chart.measure, summary: summary, partial: !!chart.partial, omitted: chart.omitted || 0,
      height: chart.kind === 'hbar' ? Math.max(120, 26 * n + 36) : 260
    };
  }

  /** Shortens text with an ellipsis so it fits a maximum pixel width. */
  function fit(ctx, text, maxWidth) {
    if (ctx.measureText(text).width <= maxWidth) { return text; }
    var t = text;
    while (t.length > 1 && ctx.measureText(t + '…').width > maxWidth) { t = t.slice(0, -1); }
    return t + '…';
  }

  /** Returns the widest measured width among the given texts. */
  function widest(ctx, texts) {
    return texts.reduce(function (m, t) { return Math.max(m, ctx.measureText(t).width); }, 0);
  }

  /** Draws a vertical chart (line or bar) onto the canvas context. */
  function draw(ctx, m, w, h) {
    ctx.fillStyle = '#fff';
    ctx.fillRect(0, 0, w, h);
    ctx.font = '12px sans-serif';
    ctx.textBaseline = 'middle';
    ctx.lineWidth = 1;
    if (m.kind === 'hbar') { drawHorizontal(ctx, m, w, h); return; }
    var legendHeight = m.series.length > 1 ? 24 : 0;
    var tickTexts = m.axis.ticks.map(m.format);
    var left = 10 + widest(ctx, tickTexts);
    var x0 = left, x1 = w - 14, y0 = 14 + legendHeight, y1 = h - 34;
    var range = m.axis.max - m.axis.min || 1;
    /** Maps a data value to a vertical canvas coordinate. */
    function yOf(v) { return y1 - (v - m.axis.min) / range * (y1 - y0); }
    ctx.textAlign = 'right';
    m.axis.ticks.forEach(function (tick, i) {
      var y = yOf(tick);
      ctx.strokeStyle = tick === 0 ? '#555' : '#e2e6ea';
      ctx.beginPath(); ctx.moveTo(x0, y); ctx.lineTo(x1, y); ctx.stroke();
      ctx.fillStyle = '#444';
      ctx.fillText(tickTexts[i], x0 - 6, y);
    });
    var n = m.labels.length;
    var slot = (x1 - x0) / n;
    var centerOf = function (i) { return x0 + (i + 0.5) * slot; };
    ctx.textAlign = 'center';
    ctx.fillStyle = '#444';
    var labelWidth = widest(ctx, m.labels) + 8;
    var every = Math.max(1, Math.ceil(labelWidth / slot));
    m.labels.forEach(function (label, i) {
      if (i % every === 0) { ctx.fillText(fit(ctx, label, Math.max(slot * every - 4, 20)), centerOf(i), y1 + 14); }
    });
    var showValues = n <= 12 && m.series.length === 1;
    if (m.kind === 'bar') {
      var barWidth = Math.min(slot * 0.7, 48);
      m.series[0].values.forEach(function (v, i) {
        if (v === null) { return; }
        ctx.fillStyle = m.series[0].color;
        ctx.fillRect(centerOf(i) - barWidth / 2, Math.min(yOf(v), yOf(0)), barWidth, Math.abs(yOf(v) - yOf(0)));
        if (showValues) { ctx.fillStyle = '#222'; ctx.fillText(m.format(v), centerOf(i), (v >= 0 ? yOf(v) - 9 : yOf(v) + 9)); }
      });
    } else {
      m.series.forEach(function (s) {
        ctx.strokeStyle = s.color; ctx.fillStyle = s.color; ctx.lineWidth = 2; ctx.setLineDash(s.dash);
        ctx.beginPath();
        var started = false;
        s.values.forEach(function (v, i) {
          if (v === null) { started = false; return; }
          if (!started) { ctx.moveTo(centerOf(i), yOf(v)); started = true; } else { ctx.lineTo(centerOf(i), yOf(v)); }
        });
        ctx.stroke();
        ctx.setLineDash([]);
        s.values.forEach(function (v, i) {
          if (v === null) { return; }
          ctx.beginPath(); ctx.arc(centerOf(i), yOf(v), 3, 0, Math.PI * 2); ctx.fill();
          if (showValues) { ctx.fillStyle = '#222'; ctx.fillText(m.format(v), centerOf(i), yOf(v) - 10); ctx.fillStyle = s.color; }
        });
      });
      ctx.lineWidth = 1;
    }
    if (legendHeight) {
      var x = x0;
      ctx.textAlign = 'left';
      m.series.forEach(function (s) {
        ctx.strokeStyle = s.color; ctx.lineWidth = 2; ctx.setLineDash(s.dash);
        ctx.beginPath(); ctx.moveTo(x, 12); ctx.lineTo(x + 24, 12); ctx.stroke();
        ctx.setLineDash([]);
        ctx.fillStyle = '#222';
        ctx.fillText(s.name, x + 30, 12);
        x += 30 + ctx.measureText(s.name).width + 16;
      });
      ctx.lineWidth = 1;
    }
  }

  /** Draws a horizontal bar chart onto the canvas context. */
  function drawHorizontal(ctx, m, w, h) {
    var maxLabel = Math.min(widest(ctx, m.labels), 150);
    var x0 = 10 + maxLabel + 8, x1 = w - 60, y0 = 8, y1 = h - 8;
    var n = m.labels.length;
    var rowHeight = (y1 - y0) / n;
    var range = m.axis.max - m.axis.min || 1;
    /** Maps a data value to a horizontal canvas coordinate. */
    function xOf(v) { return x0 + (v - m.axis.min) / range * (x1 - x0); }
    ctx.strokeStyle = '#555';
    ctx.beginPath(); ctx.moveTo(xOf(0), y0); ctx.lineTo(xOf(0), y1); ctx.stroke();
    m.series[0].values.forEach(function (v, i) {
      var cy = y0 + (i + 0.5) * rowHeight;
      ctx.textAlign = 'right'; ctx.fillStyle = '#444';
      ctx.fillText(fit(ctx, m.labels[i], maxLabel), x0 - 8, cy);
      if (v === null) { return; }
      var barHeight = Math.min(rowHeight * 0.7, 22);
      ctx.fillStyle = m.series[0].color;
      ctx.fillRect(Math.min(xOf(v), xOf(0)), cy - barHeight / 2, Math.abs(xOf(v) - xOf(0)), barHeight);
      ctx.textAlign = 'left'; ctx.fillStyle = '#222';
      ctx.fillText(m.format(v), Math.max(xOf(v), xOf(0)) + 6, cy);
    });
  }

  return { wants: wants, niceTicks: niceTicks, model: model, draw: draw };
})();
// END chartKit

// BEGIN explainText
/** Builds the plain-language explanation of the executed query from the server structured explanation. */
function explainText(e, over, fmt, lang) {
  var X_EN = {
    among: 'Among the records you can see', records: '{n} records', record: 'a specific record', or: 'or',
    by: 'by {field}', asc: 'ascending', desc: 'descending', top: 'first {n}', grouped: 'grouped by {fields}', aggregate: 'Aggregate',
    measures: { 'count': 'Count', 'countcolumn': 'Count of {field}', 'sum': 'Sum of {field}', 'avg': 'Average of {field}', 'min': 'Lowest {field}', 'max': 'Highest {field}' },
    ops: {
      'eq': '=', 'ne': '≠', 'gt': '>', 'ge': '≥', 'lt': '<', 'le': '≤', 'like': 'matches', 'not-like': 'does not match',
      'contains': 'contains', 'begins-with': 'starts with', 'ends-with': 'ends with',
      'not-contains': 'does not contain', 'not-begins-with': 'does not start with', 'not-ends-with': 'does not end with',
      'in': 'is one of', 'not-in': 'is not one of', 'null': 'is empty', 'not-null': 'is not empty', 'between': 'is between', 'not-between': 'is not between',
      'contain-values': 'includes', 'not-contain-values': 'does not include', 'neq': '≠',
      'eq-userid': 'is me', 'ne-userid': 'is not me', 'eq-businessid': 'is my business unit', 'ne-businessid': 'is not my business unit'
    },
    dates: {
      'today': 'Today', 'yesterday': 'Yesterday', 'tomorrow': 'Tomorrow',
      'this-week': 'This week', 'last-week': 'Last week', 'next-week': 'Next week',
      'this-month': 'This month', 'last-month': 'Last month', 'next-month': 'Next month',
      'this-year': 'This year', 'last-year': 'Last year', 'next-year': 'Next year',
      'last-seven-days': 'Last 7 days',
      'last-x-days': 'Last {n} days', 'next-x-days': 'Next {n} days', 'last-x-weeks': 'Last {n} weeks', 'next-x-weeks': 'Next {n} weeks',
      'last-x-months': 'Last {n} months', 'next-x-months': 'Next {n} months', 'last-x-years': 'Last {n} years', 'next-x-years': 'Next {n} years',
      'olderthan-x-days': 'Older than {n} days', 'olderthan-x-weeks': 'Older than {n} weeks', 'olderthan-x-months': 'Older than {n} months', 'olderthan-x-years': 'Older than {n} years',
      'on': 'is on', 'on-or-after': 'is on or after', 'on-or-before': 'is on or before',
      'last-x-hours': 'Last {n} hours', 'next-x-hours': 'Next {n} hours', 'this-fiscal-year': 'This fiscal year', 'last-fiscal-year': 'Last fiscal year', 'next-fiscal-year': 'Next fiscal year',
      'this-fiscal-period': 'This fiscal period', 'last-fiscal-period': 'Last fiscal period', 'next-fiscal-period': 'Next fiscal period'
    }
  };
  var X_TR = {
    among: 'Görebildiğiniz kayıtlar arasında', records: '{n} kayıt', record: 'belirli bir kayıt', or: 'veya',
    by: '{field} alanına göre', asc: 'artan', desc: 'azalan', top: 'ilk {n}', grouped: '{fields} alanına göre gruplanmış', aggregate: 'Toplama',
    measures: { 'count': 'Sayı', 'countcolumn': '{field} sayısı', 'sum': '{field} toplamı', 'avg': '{field} ortalaması', 'min': 'En düşük {field}', 'max': 'En yüksek {field}' },
    ops: {
      'eq': '=', 'ne': '≠', 'gt': '>', 'ge': '≥', 'lt': '<', 'le': '≤', 'like': 'şuna uyar:', 'not-like': 'şuna uymaz:',
      'contains': 'şunu içerir:', 'begins-with': 'şununla başlar:', 'ends-with': 'şununla biter:',
      'not-contains': 'şunu içermez:', 'not-begins-with': 'şununla başlamaz:', 'not-ends-with': 'şununla bitmez:',
      'in': 'şunlardan biri:', 'not-in': 'şunlardan biri değil:', 'null': 'boş', 'not-null': 'dolu', 'between': 'şu aralıkta:', 'not-between': 'şu aralıkta değil:',
      'contain-values': 'şunları içerir:', 'not-contain-values': 'şunları içermez:', 'neq': '≠',
      'eq-userid': 'ben', 'ne-userid': 'ben değil', 'eq-businessid': 'iş birimim', 'ne-businessid': 'iş birimim değil'
    },
    dates: {
      'today': 'Bugün', 'yesterday': 'Dün', 'tomorrow': 'Yarın',
      'this-week': 'Bu hafta', 'last-week': 'Geçen hafta', 'next-week': 'Gelecek hafta',
      'this-month': 'Bu ay', 'last-month': 'Geçen ay', 'next-month': 'Gelecek ay',
      'this-year': 'Bu yıl', 'last-year': 'Geçen yıl', 'next-year': 'Gelecek yıl',
      'last-seven-days': 'Son 7 gün',
      'last-x-days': 'Son {n} gün', 'next-x-days': 'Önümüzdeki {n} gün', 'last-x-weeks': 'Son {n} hafta', 'next-x-weeks': 'Önümüzdeki {n} hafta',
      'last-x-months': 'Son {n} ay', 'next-x-months': 'Önümüzdeki {n} ay', 'last-x-years': 'Son {n} yıl', 'next-x-years': 'Önümüzdeki {n} yıl',
      'olderthan-x-days': '{n} günden eski', 'olderthan-x-weeks': '{n} haftadan eski', 'olderthan-x-months': '{n} aydan eski', 'olderthan-x-years': '{n} yıldan eski',
      'on': 'şu tarihte:', 'on-or-after': 'şu tarihte ya da sonrasında:', 'on-or-before': 'şu tarihte ya da öncesinde:',
      'last-x-hours': 'Son {n} saat', 'next-x-hours': 'Önümüzdeki {n} saat', 'this-fiscal-year': 'Bu mali yıl', 'last-fiscal-year': 'Geçen mali yıl', 'next-fiscal-year': 'Gelecek mali yıl',
      'this-fiscal-period': 'Bu mali dönem', 'last-fiscal-period': 'Geçen mali dönem', 'next-fiscal-period': 'Gelecek mali dönem'
    }
  };
  var X = lang === 'tr' ? X_TR : X_EN;
  if (over) { Object.keys(over).forEach(function (k) { X[k] = over[k]; }); }

  /** Replaces placeholders in a text template with the given values. */
  function fill(s, v) { return String(s).replace(/\{(\w+)\}/g, function (m, k) { return v[k] === undefined ? m : v[k]; }); }
  /** Formats an ISO day with the supplied formatter or returns it unchanged. */
  function day(iso) { return fmt ? fmt(iso) : iso; }

  /** Renders a filter value as its label or raw value, never writing record ids. */
  function valueText(f) {
    var named = [];
    var unnamed = 0;
    (f.values || []).forEach(function (v) {
      var s = v.label || v.value;
      if (s) { named.push(s); } else { unnamed++; }
    });
    if (named.length === 0 && unnamed > 0) { return unnamed === 1 ? X.record : fill(X.records, { n: unnamed }); }
    return named.join(', ');
  }

  /** Renders one explanation filter as readable text. */
  function filterText(f) {
    var label = f.fieldLabel;
    var phrase = X.dates[f.op];
    var n = f.values && f.values.length ? f.values[0].value : '';
    var text;
    if (phrase && (f.from || f.to)) {
      if (f.op === 'on') { text = label + ' ' + fill(phrase, { n: n }) + ' ' + day(f.from); }
      else if (f.op === 'on-or-after') { text = label + ' ' + fill(phrase, { n: n }) + ' ' + day(f.from); }
      else if (f.op === 'on-or-before') { text = label + ' ' + fill(phrase, { n: n }) + ' ' + day(f.to); }
      else {
        var range;
        if (f.from && f.to && f.from === f.to) { range = '(' + day(f.from) + ')'; }
        else if (f.from && f.to) { range = '(' + day(f.from) + ' – ' + day(f.to) + ')'; }
        else if (f.from) { range = '(≥ ' + day(f.from) + ')'; }
        else { range = '(≤ ' + day(f.to) + ')'; }
        text = label + ': ' + fill(phrase, { n: n }) + ' ' + range + ' ≈';
      }
    } else {
      var hasN = n !== '' && n !== null && n !== undefined;
      var word = X.ops[f.op] || (phrase && (hasN || phrase.indexOf('{n}') < 0) ? fill(phrase, { n: n }) : f.op);
      var numbered = !X.ops[f.op] && phrase && phrase.indexOf('{n}') >= 0 && hasN;
      var vals = f.op === 'null' || f.op === 'not-null' || numbered ? '' : valueText(f);
      text = label + ' ' + word + (vals ? ' ' + vals : '');
    }
    return f.via ? f.via + ': ' + text : text;
  }

  var parts = [];
  var groups = {};
  var groupIds = {};
  (e.filters || []).forEach(function (f) {
    if (f.group) {
      if (!groups[f.group]) { groups[f.group] = []; groupIds[f.group] = []; parts.push({ group: f.group }); }
      groups[f.group].push(filterText(f));
      groupIds[f.group].push(f.id);
    } else {
      parts.push({ text: filterText(f), ids: [f.id] });
    }
  });
  var items = parts.map(function (p) {
    return p.group ? { text: '(' + groups[p.group].join(' ' + X.or + ' ') + ')', ids: groupIds[p.group] } : { text: p.text, ids: p.ids };
  });
  var lines = items.map(function (i) { return i.text; });

  /** Renders an aggregate measure as readable text. */
  function measureText(m) { return fill(X.measures[m.fn] || X.measures.count, { field: m.fieldLabel || '' }); }
  if (e.aggregate) {
    var head = (e.measures || []).length ? e.measures.map(measureText).join(', ') : X.aggregate;
    lines.push(e.groupBy && e.groupBy.length ? head + ' ' + fill(X.grouped, { fields: e.groupBy.join(', ') }) : head);
  }
  (e.sort || []).forEach(function (s) {
    var what = s.fn ? measureText({ fn: s.fn, fieldLabel: s.label }) : s.label;
    lines.push(fill(X.by, { field: what }) + ' ' + (s.descending ? X.desc : X.asc));
  });
  if (e.top) { lines.push(fill(X.top, { n: e.top })); }

  var summary = e.entityLabel + (lines.length ? ' · ' + lines.join(' · ') : '');
  if (summary.length > 160) { summary = summary.substring(0, 159) + '…'; }
  return { summary: summary, lines: lines.concat([X.among]), items: items };
}
// END explainText

  // BEGIN draftMailto
  /** Builds a mailto link for an email draft, flagging it too long instead of truncating when the encoded address exceeds the limit. */
  function draftMailto(to, subject, body, limit) {
    var address = /^[^\s@<>"',;:?&=#%]+@[^\s@<>"',;:?&=#%]+$/.test(to || '') ? to : '';
    var href = 'mailto:' + encodeURIComponent(address).replace('%40', '@') + '?subject=' + encodeURIComponent(String(subject || ''))
      + '&body=' + encodeURIComponent(String(body || '').replace(/\r?\n/g, '\r\n'));
    return { href: href, tooLong: href.length > limit };
  }
  // END draftMailto

  // BEGIN draftParts
  /** Splits draft text into parts so unfilled placeholder markers can be highlighted, with text always written via textContent. */
  function draftParts(text, markers) {
    var parts = [{ text: String(text || ''), mark: false }];
    (markers || []).forEach(function (marker) {
      if (!marker) { return; }
      var next = [];
      parts.forEach(function (part) {
        if (part.mark) { next.push(part); return; }
        part.text.split(marker).forEach(function (piece, i) {
          if (i > 0) { next.push({ text: marker, mark: true }); }
          if (piece) { next.push({ text: piece, mark: false }); }
        });
      });
      parts = next;
    });
    return parts;
  }
  // END draftParts

  // BEGIN columnLabel
  /** Returns the readable column label supplied by the server for a table column, falling back to the column key. */
  function columnLabel(tb, i) {
    return tb.labels && tb.labels[i] ? tb.labels[i] : tb.columns[i];
  }
  // END columnLabel

  // BEGIN tableActions
  /** Builds the actions under a result table, showing only Open in CRM when the result was truncated. */
  function tableActions(d, crmFailed) {
    var crm = !!(d && d.openInCrm);
    return { crmButton: crm, resultLink: !crm || !!crmFailed };
  }
  // END tableActions

  // BEGIN presentation
  /** Detects pure presentation requests such as 'show as chart' or 'show the table' that should not be sent as new questions. */
  function presentation(text) {
    var f = String(text || '').toLowerCase().replace(/[İIıi]/g, 'i').replace(/[Şş]/g, 's').replace(/[Ğğ]/g, 'g')
      .replace(/[Üü]/g, 'u').replace(/[Öö]/g, 'o').replace(/[Çç]/g, 'c').replace(/[^a-z0-9 ]/g, ' ');
    var tokens = f.split(' ').filter(function (x) { return x; });
    if (!tokens.length || tokens.length > 8) { return null; }
    var VIEW = { grafik: 1, grafigi: 1, grafige: 1, cizelge: 1, chart: 1, graph: 1, tablo: 1, tabloyu: 1, tabloya: 1, table: 1, cubuk: 1, cizgi: 1, bar: 1, line: 1 };
    var FILLER = { bunu: 1, bunlari: 1, sonucu: 1, sonuclari: 1, olarak: 1, halinde: 1, seklinde: 1, ver: 1, goster: 1, yap: 1, cevir: 1, donustur: 1, yapar: 1, verir: 1, gosterir: 1, misin: 1, misiniz: 1, mi: 1, lutfen: 1, show: 1, give: 1, make: 1, it: 1, this: 1, as: 1, a: 1, the: 1, me: 1, please: 1, these: 1, results: 1, result: 1, in: 1, to: 1, into: 1 };
    var hasView = false;
    var table = false;
    var style = null;
    for (var i = 0; i < tokens.length; i++) {
      var k = tokens[i];
      if (VIEW[k]) {
        hasView = true;
        if (k.indexOf('tablo') === 0 || k === 'table') { table = true; }
        if (k === 'cubuk' || k === 'bar') { style = 'bar'; }
        if (k === 'cizgi' || k === 'line') { style = 'line'; }
      } else if (!FILLER[k]) {
        return null;
      }
    }
    if (!hasView) { return null; }
    return { view: table ? 'table' : 'chart', style: style };
  }
  // END presentation

  // BEGIN parseSlash
  /** Parses the input text for the slash-command menu, which opens only when the text starts with a slash and has no spaces. */
  function parseSlash(text) {
    var m = /^\/([a-zA-Z]*)$/.exec(text);
    return m ? { open: true, query: m[1].toLowerCase() } : { open: false, query: '' };
  }
  // END parseSlash

  // BEGIN filterCommands
  /** Filters slash commands by their leading characters, hiding those not available in the current context. */
  function filterCommands(commands, query, ctx) {
    return commands.filter(function (c) { return c.name.indexOf(query) === 0 && c.available(ctx); });
  }
  // END filterCommands

  // BEGIN stopPlan
  /** Decides what Stop undoes: a new question's text returns to the input, a clarification round only removes the choice bubble. */
  function stopPlan(isAnswerRound, text) {
    return { restoreText: !isAnswerRound && text ? text : null, removeYou: true };
  }
  // END stopPlan

  // BEGIN tooSoonAfterStop
  /** Tells whether a second click on Stop came so soon after the first that it should not resend the question. */
  function tooSoonAfterStop(now, lastStopAt) {
    return now - lastStopAt < 500;
  }
  // END tooSoonAfterStop

  var pending = null;
  var lastStopAt = 0;

  /** Removes a message bubble from the chat log. */
  function removeMessage(inner) {
    if (inner && inner.parentNode && inner.parentNode.parentNode) { inner.parentNode.remove(); }
  }

  /** Aborts the in-flight request and restores the input, undoing what the Stop plan says. */
  function stop() {
    var p = pending;
    if (!p) { return; }
    pending = null;
    lastStopAt = Date.now();
    p.stopped = true;
    p.controller.abort();
    if (p.prev) { currentQuestion = p.prev.question; answers = p.prev.answers; }
    var plan = stopPlan(p.round, p.text);
    removeMessage(p.slot);
    if (plan.removeYou) { removeMessage(p.you); }
    setBusy(false);
    if (p.token === state.viewToken) {
      if (plan.restoreText) { ui.input.value = plan.restoreText; }
      ui.input.focus();
    }
    toast(t('stopped'));
  }

  var menu = { items: [], index: 0, open: false };

  /** Builds the context used to decide which slash commands are available. */
  function commandContext() {
    return { hasChat: state.currentId !== null || !!state.lastQuestion, hasQuestion: !!state.lastQuestion };
  }

  var COMMANDS = [
    { name: 'help', descKey: 'cmdHelp', available: function () { return true; }, run: showHelp },
    { name: 'new', descKey: 'cmdNew', available: function (c) { return c.hasChat; }, run: function () { newChat(); } },
    { name: 'edit', descKey: 'cmdEdit', available: function (c) { return c.hasQuestion; }, run: editLastQuestion },
    { name: 'history', descKey: 'cmdHistory', available: function () { return true; }, run: openHistory },
    { name: 'clear', descKey: 'cmdClear', available: function () { return true; }, run: function () { deleteAllChats(); } }
  ];

  /** Hides the slash-command menu, clears its items and resets the input's accessibility attributes so the menu no longer appears expanded. */
  function closeMenu() {
    menu.open = false;
    menu.items = [];
    if (ui.menu) { ui.menu.style.display = 'none'; ui.menu.replaceChildren(); }
    if (ui.input) { ui.input.setAttribute('aria-expanded', 'false'); ui.input.removeAttribute('aria-activedescendant'); }
  }

  /** Redraws the slash-command menu rows from the current filtered items, highlighting the selected row or showing a no-such-command note when empty. */
  function renderMenu() {
    ui.menu.replaceChildren();
    if (menu.items.length === 0) { ui.menu.append(el('div', t('noSuchCommand'), 'cmdnone')); }
    menu.items.forEach(function (c, i) {
      var row = el('div', undefined, i === menu.index ? 'cmdrow sel' : 'cmdrow');
      row.id = 'nlq-cmd-' + c.name;
      row.setAttribute('role', 'option');
      row.setAttribute('aria-selected', i === menu.index ? 'true' : 'false');
      row.append(el('span', '/' + c.name, 'cmdname'), el('span', t(c.descKey), 'muted'));
      row.onmousedown = function (e) { if (e.button === 0) { runCommand(c); } };
      ui.menu.append(row);
    });
    ui.menu.style.display = 'block';
    ui.input.setAttribute('aria-expanded', 'true');
    if (menu.items.length) { ui.input.setAttribute('aria-activedescendant', 'nlq-cmd-' + menu.items[menu.index].name); }
    else { ui.input.removeAttribute('aria-activedescendant'); }
  }

  /** Recomputes and redraws the slash-command menu from the current input text. */
  function refreshMenu() {
    var parsed = parseSlash(ui.input.value);
    if (busy || !parsed.open) { closeMenu(); return; }
    menu.items = filterCommands(COMMANDS, parsed.query, commandContext());
    if (menu.index >= menu.items.length) { menu.index = 0; }
    menu.open = true;
    renderMenu();
  }

  /** Moves the highlighted slash-command menu item up or down. */
  function moveMenu(step) {
    if (!menu.items.length) { return; }
    menu.index = (menu.index + step + menu.items.length) % menu.items.length;
    renderMenu();
  }

  /** Runs the chosen slash command without calling the ask path, so no model, quota or logging is involved. */
  function runCommand(c) {
    ui.input.value = '';
    closeMenu();
    c.run();
  }

  /** Shows a temporary help message that is not written to the chat or server and disappears when the chat changes. */
  function showHelp() {
    clearEmpty();
    var slot = addMessage('bot');
    slot.append(el('div', t('helpTitle'), 'question'), el('div', t('helpCommands'), 'muted'));
    var list = el('div', undefined, 'helplist');
    COMMANDS.forEach(function (c) {
      var row = el('div', undefined, 'cmdrow');
      row.append(el('span', '/' + c.name, 'cmdname'), el('span', t(c.descKey), 'muted'));
      list.append(row);
    });
    slot.append(list, el('div', t('helpExamples'), 'muted'));
    var chips = el('div', undefined, 'chips');
    (state.exampleQuestions || tv('examples')).forEach(function (text) {
      var b = el('button', text, 'chipbtn');
      b.onclick = function () { ui.input.value = text; ui.input.focus(); };
      chips.append(b);
    });
    slot.append(chips);
    scrollDown();
  }

  /** Puts the last answered question back in the input for editing without deleting the earlier turn. */
  function editLastQuestion() {
    ui.input.value = state.lastQuestion;
    ui.input.focus();
    ui.input.setSelectionRange(ui.input.value.length, ui.input.value.length);
  }

  /** Opens the chat history list and focuses the search box. */
  function openHistory() {
    if (isSideClosed()) { prefs.side = 'open'; savePrefs(); applyWindow(); }
    ui.search.focus();
  }

  /** Starts a new empty chat draft that is created on the server when the first question is asked. */
  function newChat() {
    state.viewToken++;
    state.currentId = null;
    state.createDraft = null;
    state.currentTitle = '';
    state.olderCursor = null;
    currentQuestion = '';
    state.lastQuestion = '';
    answers = {};
    ui.title.value = t('newChatTitle');
    ui.title.readOnly = true;
    showEmpty();
    renderList();
    if (ui.input) { ui.input.focus(); }
  }

  /** Shows the empty-chat greeting and example questions. */
  function showEmpty() {
    ui.log.replaceChildren();
    var box = el('div', undefined, 'empty');
    box.append(el('h2', state.displayName ? t('greetingNamed', { name: state.displayName }) : t('greeting')));
    box.append(el('div', t('greetingHint')));
    var chips = el('div', undefined, 'chips');
    (state.exampleQuestions || tv('examples')).forEach(function (text) {
      var b = el('button', text, 'chipbtn');
      b.onclick = function () { ui.input.value = text; ui.input.focus(); };
      chips.append(b);
    });
    box.append(chips);
    ui.log.append(box);
  }

  /** Opens an existing chat and loads its latest messages. */
  async function openChat(chat) {
    var token = ++state.viewToken;
    state.currentId = chat.id;
    state.currentTitle = chat.title;
    state.olderCursor = null;
    state.lastQuestion = '';
    ui.title.value = chat.title;
    ui.title.readOnly = false;
    renderList();
    ui.log.replaceChildren(skeletonRows(4), el('div', t('loading'), 'edge'));
    var r;
    try {
      r = await api('GET', '/api/chats/' + chat.id + '/messages?take=20');
    } catch (e) { if (token === state.viewToken) { showLoadError(function () { openChat(chat); }); } return; }
    if (token !== state.viewToken) { return; }
    if (r.status === 404) { toast(t('chatGone')); newChat(); loadChats(true); return; }
    if (!r.ok) { showLoadError(function () { openChat(chat); }); return; }
    ui.log.replaceChildren();
    state.olderCursor = r.json.nextCursor || null;
    if (!state.olderCursor) { ui.log.append(el('div', t('startOfChat'), 'edge')); }
    var frag = document.createDocumentFragment();
    var lastBot = null;
    r.json.items.forEach(function (item) { lastBot = historyMessage(frag, item); });
    if (r.json.items.length) { state.lastQuestion = r.json.items[r.json.items.length - 1].question; }
    ui.log.append(frag);
    scrollDown();
    if (lastBot) { lastBot.run(); }
  }

  /** Shows a chat load error with a retry action. */
  function showLoadError(retry) {
    ui.log.replaceChildren();
    var box = el('div', undefined, 'empty');
    box.append(el('div', t('loadFailed'), 'error'));
    var b = el('button', t('retry'), 'btn'); b.onclick = retry;
    box.append(b);
    ui.log.append(box);
  }

  /** Builds a history message with the question, entity and time, whose run function loads its table. */
  function historyMessage(parent, item) {
    var you = makeMessage('you');
    you.inner.textContent = item.question;
    var bot = makeMessage('bot');
    var when = el('span', item.entity + ' · ' + new Date(item.createdAt).toLocaleString(currentLocale()) + ' ', 'muted');
    var show = el('button', t('show'), 'btn');
    bot.inner.append(when, show);
    var handle = { run: function () { showEarlier(bot.inner, item.id, item.feedback, item.question); } };
    show.onclick = handle.run;
    parent.append(you.wrap, bot.wrap);
    return handle;
  }

  /** Loads and shows the table of an earlier answer in place. */
  async function showEarlier(slot, id, feedback, question) {
    slot.replaceChildren(el('span', undefined, 'spin'), el('span', t('running'), 'muted'));
    try {
      var r = await api('GET', '/api/result/' + encodeURIComponent(id));
      slot.replaceChildren();
      if (!r.json) { throw new Error('status ' + r.status); }
      r.json.feedback = feedback || null;
      r.json.question = question || '';
      render(slot, r.json);
    } catch (e) {
      slot.replaceChildren(el('span', t('unreachable', { msg: e.message }), 'error'));
    }
  }

  /** Loads the previous group of 20 older messages when scrolling up while keeping the reading position. */
  async function loadOlder() {
    if (!state.olderCursor || state.loadingOlder || state.currentId === null) { return; }
    state.loadingOlder = true;
    var token = state.viewToken;
    var chatId = state.currentId;
    var loader = el('div', undefined, 'edge');
    loader.append(el('span', undefined, 'spin'), el('span', t('loadingEarlier')));
    ui.log.prepend(loader);
    try {
      var r = await api('GET', '/api/chats/' + chatId + '/messages?take=20&cursor=' + encodeURIComponent(state.olderCursor));
      if (token !== state.viewToken) { return; }
      if (!r.ok) { throw new Error('status ' + r.status); }
      var before = ui.log.scrollHeight;
      loader.remove();
      var frag = document.createDocumentFragment();
      r.json.items.forEach(function (item) { historyMessage(frag, item); });
      state.olderCursor = r.json.nextCursor || null;
      if (!state.olderCursor) { frag.prepend(el('div', t('startOfChat'), 'edge')); }
      ui.log.prepend(frag);
      ui.log.scrollTop += ui.log.scrollHeight - before;
    } catch (e) {
      loader.replaceChildren(el('span', t('loadFailed'), 'error'));
      var again = el('button', t('retry'), 'btn'); again.style.marginLeft = '6px';
      again.onclick = function () { loader.remove(); loadOlder(); };
      loader.append(again);
    } finally {
      state.loadingOlder = false;
    }
  }

  /** Returns the open CRM record context unless the user dismissed it, reading the page address again after navigation. */
  function currentContext() {
    var rec = parseRecord(location.href);
    if (!rec || rec.id === dismissedRecord) { return null; }
    return rec;
  }

  /** Updates the chip that shows the open record context. */
  function updateChip() {
    if (!ui.chip) { return; }
    var rec = currentContext();
    ui.chip.replaceChildren();
    ui.chip.style.display = rec ? 'flex' : 'none';
    if (!rec) { return; }
    ui.chip.append(el('span', t('chip', { entity: rec.entity })));
    var off = el('button', '×', 'btn');
    off.title = t('chipOff');
    off.onclick = function () { dismissedRecord = rec.id; updateChip(); };
    ui.chip.append(off);
  }

  /** Validates the input and starts asking a question. */
  function ask() {
    var text = ui.input.value.trim();
    if (!text || busy || tooSoonAfterStop(Date.now(), lastStopAt)) { return; }
    if (parseSlash(text).open) { refreshMenu(); return; }
    var pres = presentation(text);
    if (pres) { applyPresentation(text, pres); return; }
    var prev = { question: currentQuestion, answers: answers };
    ui.input.value = '';
    currentQuestion = text;
    answers = {};
    clearEmpty();
    var you = addMessage('you');
    you.textContent = text;
    run({ you: you, text: text, round: false, prev: prev });
  }

  /** Applies a presentation request by toggling table or chart on the latest answer without contacting the server, quota or model. */
  function applyPresentation(text, pres) {
    ui.input.value = '';
    clearEmpty();
    var you = addMessage('you');
    you.textContent = text;
    var bot = addMessage('bot');
    if (!lastView || !ui.log.contains(lastView.slot)) {
      bot.append(el('div', t('presentNothing'), 'muted'));
    } else if (pres.view === 'table') {
      if (lastView.show) { lastView.show(false); }
      bot.append(el('div', t('presentTable'), 'muted'));
    } else if (lastView.show) {
      lastView.show(true);
      var kind = lastView.d.chart ? lastView.d.chart.kind : '';
      bot.append(el('div', t('presentChart'), 'muted'));
      if (pres.style === 'line' && kind !== 'line') { bot.append(el('div', t('presentLineNote'), 'muted')); }
      if (pres.style === 'bar' && kind === 'line') { bot.append(el('div', t('presentBarNote'), 'muted')); }
    } else {
      var reason = lastView.d.chartReason;
      bot.append(el('div', reason === 'few' ? t('chartNoneFew') : reason === 'list' ? t('chartNoneList') : reason === 'chain' ? t('chartNoneChain') : t('chartNoneUnsupported'), 'muted'));
    }
    scrollDown();
  }

  /** Removes the empty-chat greeting from the log. */
  function clearEmpty() {
    var empty = ui.log.querySelector('.empty');
    if (empty) { ui.log.replaceChildren(); }
  }

  /** Sends one ask round to the server and renders the outcome; info says whether it is a clarification round or a new question. */
  async function run(info) {
    info = info || {};
    var token = state.viewToken;
    var slot = addMessage('bot');
    slot.append(el('span', undefined, 'spin'), el('span', t('thinking'), 'muted'));
    var current = { controller: new AbortController(), you: info.you || null, slot: slot, text: info.text || '', round: !!info.round, token: token, prev: info.prev || null, stopped: false };
    pending = current;
    setBusy(true);
    scrollDown();
    var data = null;
    try {
      var target = info.edit
        ? ['/api/result/' + encodeURIComponent(info.edit.resultId) + '/explain/edit', { chipId: info.edit.chipId, action: info.edit.action, value: info.edit.value }]
        : info.suggest
        ? ['/api/result/' + encodeURIComponent(info.suggest.resultId) + '/suggest/apply', { suggestionId: info.suggest.id }]
        : ['/api/ask', { question: currentQuestion, answers: answers, chatId: state.currentId, context: currentContext(), createDraftId: state.createDraft || undefined }];
      var r = await api('POST', target[0], target[1], current.controller.signal);
      data = r.json || { kind: 'failure', message: t('unreachable', { msg: 'status ' + r.status }) };
    } catch (e) {
      data = { kind: 'failure', message: t('unreachable', { msg: e.message }) };
    }
    if (current.stopped) { return; }
    pending = null;
    setBusy(false);
    if (token !== state.viewToken) { loadChats(true); return; }
    if (!info.edit && !info.suggest) { state.lastQuestion = currentQuestion; }
    slot.replaceChildren();
    data.question = currentQuestion;
    data.preferChart = !!(info.suggest && info.suggest.kind === 'group');
    render(slot, data);
    if (data.kind === 'answer' || data.kind === 'summary' || data.kind === 'draft') { state.createDraft = null; }
    if ((data.kind === 'answer' || data.kind === 'summary' || data.kind === 'draft') && data.chatId) {
      var created = state.currentId !== data.chatId;
      state.currentId = data.chatId;
      ui.title.readOnly = false;
      if (created) { state.olderCursor = null; }
      loadChats(true).then(function () {
        var chat = state.chats.filter(function (c) { return c.id === data.chatId; })[0];
        if (chat) { state.currentTitle = chat.title; ui.title.value = chat.title; }
      });
    }
    scrollDown();
  }

  /** Fetches the total record count for a truncated result and updates the warning note. */
  async function loadCount(resultId, warn, shown) {
    try {
      var r = await api('GET', '/api/result/' + encodeURIComponent(resultId) + '/count');
      if (!r.ok || !r.json || typeof r.json.total !== 'number') { return; }
      var total = r.json.total;
      var entity = r.json.entityLabel ? r.json.entityLabel + ' ' : '';
      if (!r.json.limitExceeded && total <= shown) {
        warn.className = 'muted';
        warn.textContent = t('countAll', { total: total.toLocaleString(currentLocale()), entity: entity });
        return;
      }
      var numbers = { shown: shown.toLocaleString(currentLocale()), total: total.toLocaleString(currentLocale()), entity: entity };
      warn.textContent = r.json.limitExceeded ? t('countMore', numbers) : t('countOf', numbers);
    } catch (e) {  }
  }

  /** Builds the narrowing suggestion row, where option labels send a completed question as a follow-up and city or date labels put a template in the input. */
  function suggestionBox(d) {
    var box = el('div', undefined, 'narrow');
    var list = (d.suggestions || []).filter(function (s) { return s.question || d.resultId; });
    if (!list.length) { return box; }
    box.append(el('span', t('suggestionsTitle'), 'muted'));
    list.forEach(function (s) {
      var b = el('button', s.label, 'chipbtn');
      b.onclick = function () { useSuggestion(d, s); };
      box.append(b);
    });
    return box;
  }

  /** Applies a suggestion by sending it as a follow-up or filling the input. */
  function useSuggestion(d, s) {
    if (busy) { return; }
    if (s.question) {
      api('POST', '/api/suggestions/click', { kind: s.kind }).catch(function () {  });
      ui.input.value = s.question;
      ask();
      return;
    }
    var you = addMessage('you');
    you.textContent = s.label;
    run({ you: you, text: '', round: true, suggest: { resultId: d.resultId, id: s.id, kind: s.kind } });
  }

  /** Asks the server to write the user's personal CRM view and opens it in a new tab opened at click time to avoid popup blocking. */
  async function openInCrm(resultId, button, section, onFail) {
    var tab = window.open('about:blank', '_blank');
    var label = button.textContent;
    button.disabled = true;
    button.textContent = t('openingInCrm');
    var opened = false;
    try {
      var r = section
        ? await api('POST', '/api/result/' + encodeURIComponent(resultId) + '/section/open-in-crm', section)
        : await api('POST', '/api/result/' + encodeURIComponent(resultId) + '/open-in-crm');
      var url = r.ok && r.json ? String(r.json.url || '') : '';
      if (/^https?:\/\//i.test(url)) {
        opened = true;
        if (tab) { tab.opener = null; tab.location.href = url; }
        else { window.open(url, '_blank'); }
      } else {
        toast(r.status === 403 ? t('openInCrmDenied') : t('openInCrmFailed'));
      }
    } catch (e) {
      toast(t('openInCrmFailed'));
    }
    if (!opened && tab) { tab.close(); }
    if (!opened && onFail) { onFail(); }
    button.disabled = false;
    button.textContent = label;
  }

  /** Renders a server outcome into a message slot according to its kind. */
  function render(slot, d) {
    if (d.kind === 'failure') { slot.append(el('div', d.message, 'error')); }
    else if (d.kind === 'question') { renderQuestion(slot, d); }
    else if (d.kind === 'summary') { renderSummary(slot, d); }
    else if (d.kind === 'draft') { renderDraft(slot, d); }
    else if (d.kind === 'report') { renderReports(slot, d); }
    else if (d.kind === 'create') { renderCreate(slot, d); }
    else {
      renderAnswer(slot, d);
      var foot = el('div', undefined, 'answerfoot');
      slot.append(foot);
      var suggestions = suggestionBox(d);
      if (prefs.suggestions !== false && suggestions.childNodes.length) { foot.append(suggestions); }
      feedbackBar(foot, d);
      qualityBar(slot, d);
    }
  }

  var SUM_TITLES = { basic: 'sumBasic', opportunities: 'sumOpportunities', activities: 'sumActivities', cases: 'sumCases', contracts: 'sumContracts', orders: 'sumOrders' };

  var SUM_METRICS = {
    'opportunities.count': 'sumMOppCount', 'opportunities.total': 'sumMOppTotal', 'activities.last_contact': 'sumMLastContact',
    'cases.count': 'sumMCaseCount', 'cases.oldest': 'sumMCaseOldest', 'cases.priority:': 'sumMNoPriority',
    'contracts.count': 'sumMContractCount', 'contracts.expiring': 'sumMContractExpiring', 'orders.count': 'sumMOrderCount'
  };
  /** Returns the localized label of a summary metric. */
  function metricLabel(sectionKind, m) {
    var key = SUM_METRICS[sectionKind + '.' + m.key];
    if (!key) { return m.label; }
    return t(key, { n: (String(m.label).match(/\d+/) || [''])[0] });
  }
  /** Returns the formatted value of a summary metric. */
  function metricValue(sectionKind, m) {
    return sectionKind === 'activities' && m.key === 'last_contact' && m.value === 'none' ? t('sumMNone') : m.value;
  }

  /** Builds a link or button that opens a CRM record. */
  function recordLink(d, entity, id, label, asButton) {
    if (!d.recordUrlBase || !entity || !id) { return null; }
    var a = el('a', label);
    a.href = d.recordUrlBase + encodeURIComponent(entity) + '&id=' + encodeURIComponent(id);
    a.target = '_blank';
    a.rel = 'noopener noreferrer';
    if (asButton) {
      a.className = 'btn';
    } else {
      a.style.marginLeft = '8px';
    }
    return a;
  }

  /** Renders the customer summary card with collapsible sections and record links. */
  function renderSummary(slot, d) {
    if (d.aboutRecord) { slot.append(el('div', t('about', { name: d.aboutRecord }), 'muted')); }
    var box = el('div', undefined, 'sum');
    var head = el('div', undefined, 'sumhead');
    head.append(el('span', t('sumTitle', { name: d.recordName || d.entityLabel })));
    var open = recordLink(d, d.entity, d.recordId, t('sumOpenRecord'));
    if (open) { head.append(open); }
    box.append(head);
    d.sections.forEach(function (s) {
      var det = el('details', undefined, 'sumsec');
      det.open = true;
      var title = SUM_TITLES[s.kind] ? t(SUM_TITLES[s.kind]) : s.title;
      det.append(el('summary', s.via ? t('sumVia', { title: title, via: s.via }) : title));
      if (s.status === 'unavailable') { det.append(el('div', t('sumUnavailable'), 'warn')); }
      else {
        s.metrics.forEach(function (m) {
          var line = el('div', undefined, 'sumline');
          line.append(el('span', metricLabel(s.kind, m) + ': ', 'metric'), el('span', metricValue(s.kind, m)));
          det.append(line);
        });
        s.items.forEach(function (i) {
          var line = el('div', undefined, 'sumitem');
          line.append(el('span', i.text));
          if (i.detail) { line.append(el('span', ' \u00b7 ' + i.detail, 'muted')); }
          var link = recordLink(d, i.entity, i.id, t('sumOpen'));
          if (link) { line.append(link); }
          det.append(line);
        });
        if (s.status === 'empty' && s.metrics.length === 0) { det.append(el('div', t('sumEmpty'), 'muted')); }
        if (s.canOpen && d.resultId) {
          var crm = el('button', t('openInCrm'), 'btn');
          crm.title = t('openInCrmHint');
          crm.style.marginTop = '4px';
          crm.onclick = function () { openInCrm(d.resultId, crm, { kind: s.kind, entity: s.entity, viaEntity: s.viaEntity || null }); };
          det.append(crm);
        }
      }
      box.append(det);
    });
    box.append(el('div', t('sumFooter'), 'muted'));
    if (d.resultId) {
      var refresh = el('button', t('sumRefresh'), 'btn');
      refresh.onclick = async function () {
        refresh.disabled = true;
        try {
          var r = await api('GET', '/api/result/' + encodeURIComponent(d.resultId));
          if (r.ok && r.json && r.json.kind === 'summary') { slot.replaceChildren(); render(slot, r.json); return; }
        } catch (e) {  }
        refresh.disabled = false;
      };
      box.append(refresh);
    }
    slot.append(box);
  }

  var DRAFT_MAILTO_LIMIT = 1800;

  /** Writes text into a parent element, highlighting the marked placeholder parts. */
  function markedInto(parent, text, markers) {
    draftParts(text, markers).forEach(function (part) {
      parent.append(part.mark ? el('span', part.text, 'mark') : document.createTextNode(part.text));
    });
  }

  /** Selects all contents of a node so the user can copy it manually. */
  function selectContents(node) {
    try {
      var range = document.createRange();
      range.selectNodeContents(node);
      var selection = window.getSelection();
      selection.removeAllRanges();
      selection.addRange(range);
    } catch (e) {  }
  }

  /** Copies text and reports whether it succeeded, leaving the text selected and guiding the user when it fails. */
  async function copyChecked(text) {
    try {
      if (navigator.clipboard && window.isSecureContext) { await navigator.clipboard.writeText(text); return true; }
    } catch (e) {  }
    var area = document.createElement('textarea');
    area.value = text;
    area.style.cssText = 'position:fixed;left:-9999px;top:0;opacity:0';
    document.body.appendChild(area);
    area.select();
    var ok = false;
    try { ok = document.execCommand('copy'); } catch (e) { ok = false; }
    document.body.removeChild(area);
    return ok;
  }

  /** Renders the email draft card with its warning, recipient, subject, body and actions. */
  function renderDraft(slot, d) {
    if (d.aboutRecord) { slot.append(el('div', t('about', { name: d.aboutRecord }), 'muted')); }
    var box = el('div', undefined, 'draft');
    box.append(el('div', t('draftTitle', { name: d.recordName || '' }), 'sumhead'));
    box.append(el('div', t('draftWarning'), 'warn draftwarn'));
    var markers = d.unresolved || [];

    var toLine = el('div', undefined, 'draftline');
    toLine.append(el('span', t('draftTo') + ' ', 'metric'), el('span', d.to || t('draftNoRecipient'), d.to ? undefined : 'muted'));
    box.append(toLine);
    var subjectLine = el('div', undefined, 'draftline');
    subjectLine.append(el('span', t('draftSubject') + ' ', 'metric'));
    var subjectText = el('strong');
    markedInto(subjectText, d.subject, markers);
    subjectLine.append(subjectText);
    box.append(subjectLine);
    var body = el('div', undefined, 'draftbody');
    markedInto(body, d.body, markers);
    box.append(body);
    if (markers.length) { box.append(el('div', t('draftUnresolved'), 'muted')); }

    var buttons = el('div', undefined, 'draftbtns');
    var copy = el('button', t('copy'), 'btn');
    copy.onclick = async function () {
      if (await copyChecked(d.subject + '\n\n' + d.body)) {
        copy.textContent = t('copied');
        setTimeout(function () { copy.textContent = t('copy'); }, 1500);
      } else {
        selectContents(body);
        toast(t('draftCopyFailed'));
      }
    };
    buttons.append(copy);

    var mail = draftMailto(d.to, d.subject, d.body, DRAFT_MAILTO_LIMIT);
    var open = el('button', t('draftMailto'), 'btn');
    if (mail.tooLong) {
      open.disabled = true;
      open.title = t('draftMailtoTooLong');
      buttons.append(open, el('span', t('draftMailtoTooLong'), 'warn'));
    } else {
      open.onclick = function () {
        var link = document.createElement('a');
        link.href = mail.href;
        link.click();
      };
      buttons.append(open);
    }
    if (d.question) {
      var again = el('button', t('draftAgain'), 'btn');
      again.onclick = function () { ui.input.value = d.question; ask(); };
      buttons.append(again);
    }
    box.append(buttons);
    slot.append(box);
  }

  var CREATE_STATUS = { ready: 'createStatus_ready', needs_info: 'createStatus_needs_info', created: 'createStatus_created', failed: 'createStatus_failed', skipped: 'createStatus_skipped' };
  var CREATE_ERRORS = { permission: 'createErr_permission', crm: 'createErr_crm' };

  /** Turns a record-creation problem into readable text. */
  function createProblemText(p) {
    var key = 'createProb_' + p.code;
    var text = t(key, { label: p.label, detail: p.detail });
    return text === key ? p.label : text;
  }

  /** Sends a record-creation action to the server and updates the card's buttons and state. */
  async function createAct(slot, path, body, buttons) {
    buttons.forEach(function (b) { b.disabled = true; });
    var note = el('span', t('createWorking'), 'muted');
    buttons[0].parentNode.append(note);
    var r = await api('POST', path, body);
    var json = r.json || { kind: 'failure', message: t('unreachable', { msg: 'status ' + r.status }) };
    state.createDraft = null;
    slot.replaceChildren();
    render(slot, json);
    if (json.kind === 'create' && json.phase === 'done' && json.chatId && state.currentId !== json.chatId) {
      state.currentId = json.chatId;
      ui.title.readOnly = false;
      state.olderCursor = null;
    }
    if (json.kind === 'create' && json.phase === 'done') { loadChats(true); }
    scrollDown();
  }

  /** Renders the record creation preview or result card. */
  function renderCreate(slot, d) {
    var phase = d.phase;
    var rows = d.rows || [];
    var box = el('div', undefined, 'create');
    if (phase === 'cancelled') { box.append(el('div', t('createCancelled'), 'muted')); slot.append(box); return; }
    var pending = phase === 'ask' || phase === 'preview';
    if (pending) {
      state.createDraft = d.draftId;
      if (state.createButtons && state.createButtons.draft === d.draftId) { state.createButtons.list.forEach(function (b) { b.disabled = true; }); }
    }
    box.append(el('div', phase === 'done' ? t('createDoneTitle', { entity: d.entityLabel }) : t('createTitle', { entity: d.entityLabel }), 'sumhead'));
    if (phase === 'ask') {
      var missing = [];
      ((rows[0] && rows[0].problems) || []).forEach(function (p) { if (p.code === 'missing_required') { missing.push(p.label); } });
      box.append(el('div', missing.length ? t('createAskMissing', { fields: missing.join(', ') }) : t('createAskFix'), 'warn'));
    }
    if (pending && rows.length > 1) {
      var ready = rows.filter(function (r) { return r.status === 'ready'; }).length;
      box.append(el('div', t('createRowsSummary', { ready: ready, total: rows.length }), 'muted'));
    }
    var anyDuplicate = false;
    var readyCount = 0;
    rows.forEach(function (r) {
      var row = el('div', undefined, 'crow');
      var line = el('div', undefined, 'cname');
      line.append(el('strong', r.name));
      var openButton = null;
      var cls = r.status === 'created' || r.status === 'ready' ? 'cok' : r.status === 'needs_info' ? 'cwarn' : 'cbad';
      line.append(el('span', ' · ' + t(CREATE_STATUS[r.status] || 'createStatus_failed'), cls));
      if (r.status === 'created') {
        var link = recordLink(d, d.entity, r.recordId, t('createOpen'), true);
        if (link) { openButton = link; }
      }
      row.append(line);
      (r.fields || []).forEach(function (f) {
        var l = el('div', undefined, 'cfield');
        l.append(el('span', f.label + ': ', 'metric'), el('span', f.value));
        row.append(l);
      });
      (r.problems || []).forEach(function (p) { row.append(el('div', createProblemText(p), 'cprob')); });
      if (r.error) { row.append(el('div', t(CREATE_ERRORS[r.error] || 'createErr_crm'), 'cprob')); }
      if ((r.duplicates || []).length) { anyDuplicate = true; row.append(el('div', t('createDuplicate', { names: r.duplicates.join(', ') }), 'warn')); }
      if (openButton) {
        var openRow = el('div', undefined, 'tblactions');
        openRow.append(openButton);
        row.append(openRow);
      }
      if (r.status === 'ready') { readyCount++; }
      box.append(row);
    });
    if (pending) {
      var btns = el('div', undefined, 'cbtns');
      var buttons = [];
      if (phase === 'preview' && readyCount > 0) {
        var go = el('button', anyDuplicate ? t('createAnyway') : readyCount > 1 ? t('createButtonN', { n: readyCount }) : t('createButton'), 'btn primary');
        buttons.push(go);
        btns.append(go);
      }
      var cancel = el('button', t('createCancel'), 'btn');
      buttons.push(cancel);
      btns.append(cancel);
      box.append(btns);
      state.createButtons = { draft: d.draftId, list: buttons };
      if (buttons.length > 1) {
        buttons[0].onclick = function () { createAct(slot, '/api/create/' + encodeURIComponent(d.draftId) + '/confirm', { allowDuplicates: anyDuplicate, chatId: state.currentId }, buttons); };
      }
      cancel.onclick = function () { createAct(slot, '/api/create/' + encodeURIComponent(d.draftId) + '/cancel', {}, buttons); };
    }
    slot.append(box);
  }

  var FB_REASONS = [['wrong_records', 'fbReasonWrongRecords'], ['wrong_columns', 'fbReasonWrongColumns'], ['misunderstood', 'fbReasonMisunderstood'], ['too_slow', 'fbReasonTooSlow'], ['other', 'fbReasonOther']];

  /** Builds the thumbs up and down feedback bar under an answer. */
  function feedbackBar(slot, d) {
    if (!state.feedback.enabled || !d.resultId) { return; }
    var current = d.feedback || null;
    var bar = el('div', undefined, 'fb');
    slot.append(bar);
    var formOpen = false;
    var suggestOpen = false;
    var suggested = false;
    var pending = false;

    /** Saves the feedback vote and optional details to the server. */
    async function save(payload, thanks) {
      if (pending) { return; }
      pending = true;
      try {
        var r = await api('PUT', '/api/result/' + encodeURIComponent(d.resultId) + '/feedback', payload);
        if (!r.ok) { throw new Error('status ' + r.status); }
        current = r.json.feedback;
        d.feedback = current;
        pending = false;
        draw(thanks);
      } catch (e) { pending = false; toast(t('fbFailed')); }
    }

    /** Withdraws the user's feedback vote through the delete endpoint, treating a 404 as already gone, then resets the vote state. */
    async function clear() {
      if (pending) { return; }
      pending = true;
      try {
        var r = await api('DELETE', '/api/result/' + encodeURIComponent(d.resultId) + '/feedback');
        if (!r.ok && r.status !== 404) { throw new Error('status ' + r.status); }
        current = null;
        d.feedback = null;
        formOpen = false;
        pending = false;
        draw('');
      } catch (e) { pending = false; toast(t('fbFailed')); }
    }

    /** Builds a thumbs button for the given rating. */
    function thumb(label, rating) {
      var b = el('button', label, 'xbtn fbbtn' + (current && current.rating === rating ? ' fbon' : ''));
      b.title = rating === 'up' ? t('fbUp') : t('fbDown');
      b.onclick = function () {
        if (current && current.rating === rating) { clear(); return; }
        if (rating === 'up') { formOpen = false; save({ rating: 'up' }, t('fbThanks')); }
        else { formOpen = true; save({ rating: 'down' }, ''); }
      };
      return b;
    }

    /** Builds the form shown after a thumbs-down vote with reasons and the optional sharing checkbox. */
    function form() {
      var box = el('div', undefined, 'fbform');
      box.append(el('div', t('fbReasonTitle'), 'muted'));
      var chosen = current && current.reason ? current.reason : null;
      FB_REASONS.forEach(function (pair) {
        var label = el('label');
        var radio = el('input');
        radio.type = 'radio';
        radio.name = 'fb-' + d.resultId;
        radio.checked = chosen === pair[0];
        radio.onchange = function () { chosen = pair[0]; };
        label.append(radio, el('span', t(pair[1])));
        box.append(label);
      });
      var share = null;
      if (state.feedback.allowSharing) {
        var shareLabel = el('label');
        share = el('input');
        share.type = 'checkbox';
        share.checked = false;
        shareLabel.append(share, el('span', t('fbShare')));
        box.append(shareLabel, el('div', t('fbShareWarn'), 'muted'));
      }
      var send = el('button', t('fbSend'), 'btn primary');
      send.onclick = function () {
        formOpen = false;
        var wantShare = !!(share && share.checked);
        save({ rating: 'down', reason: chosen, share: wantShare }, wantShare ? t('fbShared') : t('fbThanks'));
      };
      box.append(send);
      return box;
    }

    /** Builds the form for suggesting an answer as a good example to the acceptance set. */
    function suggestForm() {
      var box = el('div', undefined, 'fbform');
      box.append(el('div', t('sgNotice'), 'muted'));
      var send = el('button', t('sgSend'), 'btn primary');
      var cancel = el('button', t('qsCancel'), 'xbtn');
      cancel.onclick = function () { suggestOpen = false; draw(''); };
      send.onclick = async function () {
        if (pending) { return; }
        pending = true;
        send.disabled = true;
        try {
          var r = await api('POST', '/api/result/' + encodeURIComponent(d.resultId) + '/quality/suggest');
          pending = false;
          if (r.ok) { suggested = true; suggestOpen = false; draw(r.json && r.json.duplicate ? t('sgDuplicate') : t('sgDone')); }
          else { send.disabled = false; toast(r.json && r.json.error ? r.json.error : t('sgFailed')); }
        } catch (e) { pending = false; send.disabled = false; toast(t('sgFailed')); }
      };
      box.append(send, cancel);
      return box;
    }

    /** Redraws the feedback bar for the current state with an optional message. */
    function draw(message) {
      bar.replaceChildren();
      var row = el('div', undefined, 'fbrow');
      row.append(thumb('\u{1F44D}', 'up'), thumb('\u{1F44E}', 'down'));
      if (message) { row.append(el('span', message, 'muted')); }
      if (current && current.shared) {
        if (!message) { row.append(el('span', t('fbShared'), 'muted')); }
        var withdraw = el('button', t('fbWithdraw'), 'xbtn');
        withdraw.onclick = function () { save({ rating: 'down', reason: current.reason || null, share: false }, t('fbWithdrawn')); };
        row.append(withdraw);
      }
      if (current && current.rating === 'up' && !suggested && !suggestOpen && suggestEligible(d, state.quality)) {
        var suggest = el('button', t('sgAction'), 'xbtn');
        suggest.onclick = function () { suggestOpen = true; draw(''); };
        row.append(suggest);
      }
      bar.append(row);
      if (formOpen && current && current.rating === 'down') { bar.append(form()); }
      if (suggestOpen && current && current.rating === 'up') { bar.append(suggestForm()); }
    }
    draw('');
  }


  // BEGIN suggestEligible
  /** Tells whether the suggest-as-good-example option applies: a plain, non-empty, untruncated, non-chain answer for a user who cannot add to the acceptance set directly. */
  function suggestEligible(d, quality) {
    var rows = d && d.table && d.table.rows ? d.table.rows.length : 0;
    var chain = !!(d && d.stepSummaries && d.stepSummaries.length);
    return !!(quality && quality.canSuggest && !quality.canSave && d && d.resultId && rows > 0 && !d.truncated && !chain);
  }
  // END suggestEligible

  /** Builds the Add to acceptance set bar, shown only to users with Quality.Manage permission, with a required confirmation checkbox. */
  function qualityBar(slot, d) {
    var rows = d.table && d.table.rows ? d.table.rows.length : 0;
    if (!state.quality.canSave || !d.resultId || rows === 0 || d.truncated) { return; }
    var bar = el('div', undefined, 'fb');
    slot.append(bar);
    var done = false;
    var busy = false;

    /** Opens the acceptance-set options for the answer. */
    function open() {
      bar.replaceChildren();
      var box = el('div', undefined, 'fbform');
      box.append(el('div', t('qsTitle', { n: rows }), 'muted'));
      var order = 'auto';
      var columns = 'reference';
      /** Builds a group of choice buttons with a title that calls a handler when one is picked. */
      function group(title, name, options, onPick) {
        box.append(el('div', title, 'muted'));
        options.forEach(function (o, i) {
          var label = el('label');
          var radio = el('input');
          radio.type = 'radio';
          radio.name = name + '-' + d.resultId;
          radio.checked = i === 0;
          radio.onchange = function () { onPick(o[0]); };
          label.append(radio, el('span', o[1]));
          box.append(label);
        });
      }
      group(t('qsOrderTitle'), 'qo', [['auto', t('qsOrderAuto')], ['ordered', t('qsOrderOrdered')], ['unordered', t('qsOrderUnordered')]], function (v) { order = v; });
      group(t('qsColsTitle'), 'qc', [['reference', t('qsColsRef')], ['exact', t('qsColsExact')]], function (v) { columns = v; });
      var okLabel = el('label');
      var ok = el('input');
      ok.type = 'checkbox';
      ok.checked = false;
      okLabel.append(ok, el('span', t('qsConfirm')));
      box.append(okLabel);
      var save = el('button', t('qsSave'), 'btn primary');
      save.disabled = true;
      ok.onchange = function () { save.disabled = !ok.checked || busy; };
      var cancel = el('button', t('qsCancel'), 'xbtn');
      cancel.onclick = function () { draw(''); };
      save.onclick = async function () {
        if (busy || !ok.checked) { return; }
        busy = true;
        save.disabled = true;
        try {
          var r = await api('POST', '/api/result/' + encodeURIComponent(d.resultId) + '/quality/save', { order: order, columns: columns, confirmed: true });
          busy = false;
          if (r.ok) { done = true; draw(r.json && r.json.duplicate ? t('qsDuplicate') : t('qsSaved')); }
          else { toast(r.json && r.json.error ? r.json.error : t('qsFailed')); save.disabled = !ok.checked; }
        } catch (e) { busy = false; toast(t('qsFailed')); save.disabled = !ok.checked; }
      };
      box.append(save, cancel);
      bar.append(box);
    }

    /** Redraws the acceptance-set bar for the current state with an optional message. */
    function draw(message) {
      bar.replaceChildren();
      var row = el('div', undefined, 'fbrow');
      if (message) { row.append(el('span', message, 'muted')); }
      if (!done) {
        var add = el('button', t('qsAdd'), 'btn');
        add.onclick = open;
        row.append(add);
      }
      bar.append(row);
    }
    draw('');
  }

  /** Renders the report finder card listing CRM or SSRS reports, with only http or https links. */
  function renderReports(slot, d) {
    var box = el('div', undefined, 'sum');
    box.append(el('div', d.listedAll ? t('rpAll') : t('rpFound'), 'muted'));
    (d.reports || []).forEach(function (r) {
      var row = el('div', undefined, 'rprow');
      var info = el('div', undefined, 'rpinfo');
      info.append(el('div', r.name));
      if (r.description) { info.append(el('div', r.description, 'muted')); }
      if (r.forRecord) { info.append(el('div', t('rpFor', { name: r.forRecord }), 'muted')); }
      row.append(info);
      if (/^https?:\/\//i.test(String(r.url || ''))) {
        var open = el('a', t('rpOpen'), 'btn');
        open.href = r.url;
        open.target = '_blank';
        open.rel = 'noopener noreferrer';
        open.onclick = function () {
          if (d.resultId) { api('POST', '/api/reports/click', { resultId: d.resultId, reportId: r.id }).catch(function () {  }); }
        };
        row.append(open);
      }
      box.append(row);
    });
    box.append(el('div', t('rpHint'), 'muted'));
    slot.append(box);
  }

  /** Renders a clarifying question with its choices. */
  function renderQuestion(slot, d) {
    slot.append(el('div', d.text, 'question'));
    var box = el('div', undefined, 'choices');
    d.choices.forEach(function (c) {
      var b = el('button', c.label, 'btn');
      b.onclick = function () { var prev = { question: currentQuestion, answers: Object.assign({}, answers) }; answers[d.key] = c.value; var you = addMessage('you'); you.textContent = c.label; run({ you: you, text: '', round: true, prev: prev }); };
      box.append(b);
    });
    if (d.allowFreeText) {
      var input = el('input', undefined, 'txt');
      input.placeholder = d.questionKind === 'columns' ? t('columnsHint') : t('rephraseHint');
      stopKeys(input);
      var go = el('button', t('send'), 'btn');
      go.onclick = function () {
        var v = input.value.trim();
        if (!v) { return; }
        var prev = { question: currentQuestion, answers: Object.assign({}, answers) };
        var you = addMessage('you');
        you.textContent = v;
        if (d.questionKind === 'columns') { answers[d.key] = 'custom:' + v; }
        else { currentQuestion = v; answers = {}; }
        run({ you: you, text: '', round: true, prev: prev });
      };
      box.append(input, go);
    }
    slot.append(box);
  }

  /** Builds the summary box of previous chain steps, such as the selected record names, with no ids and only textContent. */
  function stepBox(s) {
    var box = el('div', undefined, 'stepbox');
    box.append(el('div', t('stepLabel', { n: s.number, question: s.question })));
    if (!s.rows) { box.append(el('div', t('stepNoRows'), 'stepline muted')); return box; }
    (s.preview || []).forEach(function (row, i) {
      var values = (s.columns || []).map(function (c, j) { return c + ': ' + row[j]; }).join(', ');
      var name = s.names && s.names[i] ? s.names[i] : '';
      var nameShown = name && row.some(function (cell) { return cell === name; });
      box.append(el('div', (nameShown ? values : (name && values ? name + ' — ' + values : name || values)), 'stepline'));
    });
    if (s.rows > (s.preview || []).length) { box.append(el('div', t('stepMore', { n: s.rows - (s.preview || []).length }), 'stepline muted')); }
    return box;
  }

  /** Formats an ISO date as a short local date, adding the year only when it is not the current year. */
  function formatDay(iso) {
    var p = /^(\d{4})-(\d{2})-(\d{2})$/.exec(iso || '');
    if (!p) { return iso; }
    try {
      var date = new Date(+p[1], +p[2] - 1, +p[3]);
      var options = { day: 'numeric', month: 'short' };
      if (date.getFullYear() !== new Date().getFullYear()) { options.year = 'numeric'; }
      return date.toLocaleDateString(currentLocale(), options);
    } catch (e) { return iso; }
  }

  /** Renders the one-line query explanation that expands to filter rows and the permission note, writing text via textContent. */
  function renderExplanation(slot, explanation, answer) {
    var x = explainText(explanation, null, formatDay, currentLanguage);
    var det = el('details', undefined, 'explain');
    if (prefs.explainOpen) { det.open = true; }
    det.append(el('summary', x.summary));
    var fixedList = explanation.filters.some(function (f) { return f.values.length >= 2 && f.values.every(function (v) { return v.value === null && v.label === null; }); });
    var editable = !!(answer && answer.resultId && !answer.fromChain && !explanation.aggregate && !fixedList);
    x.lines.forEach(function (line, i) {
      var row = el('div', undefined, 'explainline');
      row.append(el('span', line));
      var item = x.items[i];
      var filter = item && editable && item.ids.length === 1 ? explanation.filters.filter(function (f) { return f.id === item.ids[0]; })[0] : null;
      if (filter) { addEditButtons(row, line, filter, answer); }
      det.append(row);
    });
    slot.append(det);
  }

  /** Adds remove and edit buttons to a filter row of the explanation. */
  function addEditButtons(row, line, filter, answer) {
    var remove = el('button', '\u00d7', 'xbtn');
    remove.title = t('editRemove');
    remove.onclick = function () { applyEdit(answer, filter, 'remove', null); };
    row.append(remove);
    if (!filter.edit) { return; }
    var change = el('button', '\u270e', 'xbtn');
    change.title = t('editChange');
    change.onclick = function () { showEditBox(row, line, filter, answer); };
    row.append(change);
  }

  /** Shows the edit control for a filter: a dropdown for option fields or an input for number and text, with invalid values rejected by the server. */
  function showEditBox(row, line, filter, answer) {
    row.replaceChildren();
    var box = el('span', undefined, 'editbox');
    box.append(el('span', filter.fieldLabel));
    var current = filter.values.length ? (filter.values[0].value || '') : '';
    var control;
    if (filter.edit.kind === 'option') {
      control = el('select');
      filter.edit.options.forEach(function (o) {
        var opt = el('option', o.label || o.value);
        opt.value = o.value;
        if (o.value === current) { opt.selected = true; }
        control.append(opt);
      });
    } else {
      control = el('input', undefined, 'txt');
      control.value = current;
      stopKeys(control);
    }
    var apply = el('button', t('editApply'), 'btn primary');
    apply.onclick = function () { applyEdit(answer, filter, 'set', control.value); };
    var cancel = el('button', t('editCancel'), 'btn');
    cancel.onclick = function () { row.replaceChildren(el('span', line)); addEditButtons(row, line, filter, answer); };
    box.append(control, apply, cancel);
    row.append(box);
  }

  /** Sends a filter edit to the server and renders the resulting new answer. */
  function applyEdit(answer, filter, action, value) {
    if (busy) { return; }
    var you = addMessage('you');
    you.textContent = action === 'remove' ? t('editRemoved', { field: filter.fieldLabel }) : t('editChanged', { field: filter.fieldLabel });
    run({ you: you, text: '', round: true, edit: { resultId: answer.resultId, chipId: filter.id, action: action, value: value } });
  }

  /** Builds the Table and Chart switch, drawing charts on a canvas and opening on the chart when the question asked for one. */
  function chartSwitch(slot, d, tableSlot) {
    var locale = currentLocale();
    var texts = { other: t('chartOther'), empty: t('chartEmpty'), line: t('chartLine'), bar: t('chartBar') };
    var m = chartKit.model(d.chart, locale, texts);
    var bar = el('div', undefined, 'chartbar');
    var chartButton = el('button', t('chartView'), 'btn');
    var tableButton = el('button', t('tableView'), 'btn');
    var png = el('button', t('chartPng'), 'btn');
    bar.append(chartButton, tableButton, png);
    var box = el('div', undefined, 'chartbox');
    var canvas = document.createElement('canvas');
    canvas.setAttribute('role', 'img');
    canvas.setAttribute('aria-label', m.summary);
    canvas.style.cssText = 'width:100%;display:block;';
    box.append(canvas);
    if (m.partial) { box.append(el('div', t('chartPartial'), 'warn')); }
    if (m.omitted) { box.append(el('div', t('chartOmitted', { n: m.omitted }), 'muted')); }
    slot.append(bar, box, tableSlot);

    var drawn = 0;
    /** Repaints the currently selected table or chart view. */
    function paint() {
      var width = Math.max(280, Math.round(box.clientWidth || 480));
      if (drawn === width) { return; }
      drawn = width;
      var ratio = window.devicePixelRatio || 1;
      canvas.width = Math.round(width * ratio);
      canvas.height = Math.round(m.height * ratio);
      canvas.style.height = m.height + 'px';
      var ctx = canvas.getContext('2d');
      ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
      chartKit.draw(ctx, m, width, m.height);
    }
    /** Shows the given chart or hides it in the switch. */
    function show(chart) {
      box.style.display = chart ? '' : 'none';
      tableSlot.style.display = chart ? 'none' : '';
      png.style.display = chart ? '' : 'none';
      chartButton.setAttribute('aria-pressed', chart ? 'true' : 'false');
      tableButton.setAttribute('aria-pressed', chart ? 'false' : 'true');
      if (chart) { paint(); }
    }
    if (lastView && lastView.d === d) { lastView.show = show; }
    chartButton.onclick = function () { show(true); };
    tableButton.onclick = function () { show(false); };
    png.onclick = function () {
      canvas.toBlob(function (blob) {
        if (!blob) { return; }
        var link = document.createElement('a');
        link.href = URL.createObjectURL(blob);
        link.download = 'chart.png';
        link.click();
        setTimeout(function () { URL.revokeObjectURL(link.href); }, 1000);
      });
    };
    show(chartKit.wants(d.question) || !!d.preferChart);
  }

  /** Renders an answer outcome with its table, chart, notes, suggestions and actions. */
  function renderAnswer(slot, d) {
    lastView = { slot: slot, d: d, show: null };
    if (d.aboutRecord) { slot.append(el('div', t('about', { name: d.aboutRecord }), 'muted')); }
    (d.stepSummaries || []).forEach(function (s) { slot.append(stepBox(s)); });
    (d.stepNotes || []).forEach(function (n) { slot.append(el('div', n, 'warn')); });
    (d.glossary || []).forEach(function (g) { slot.append(el('div', t('glossaryLine', { term: g.term, text: g.text }), 'muted')); });
    if (d.explanation) { renderExplanation(slot, d.explanation, d); }
    else { slot.append(el('div', d.understood)); }

    if (d.fetchXml) {
      var det = el('details');
      det.append(el('summary', t('fetchXml')));
      det.append(el('pre', d.fetchXml));
      var copy = el('button', t('copy'), 'btn');
      copy.onclick = function () {
        copyText(d.fetchXml);
        copy.textContent = t('copied');
        setTimeout(function () { copy.textContent = t('copy'); }, 1500);
      };
      det.append(copy);
      slot.append(det);
    }

    if (d.truncated) {
      var warn = el('div', t('truncated'), 'warn');
      slot.append(warn);
      if (d.resultId) { loadCount(d.resultId, warn, d.table.rows.length); }
    }

    if (d.chart) {
      var tableSlot = el('div');
      chartSwitch(slot, d, tableSlot);
      renderTable(tableSlot, d);
      return;
    }
    if (d.chartReason && chartKit.wants(d.question)) {
      var reasonText = d.chartReason === 'few' ? t('chartNoneFew')
        : d.chartReason === 'list' ? t('chartNoneList')
        : d.chartReason === 'unsupported' ? t('chartNoneUnsupported')
        : d.chartReason === 'chain' ? t('chartNoneChain') : '';
      if (reasonText) { slot.append(el('div', reasonText, 'muted')); }
    }
    renderTable(slot, d);
  }

  /** Renders the answer's result table with its labels, row links and paging. */
  function renderTable(slot, d) {
    var tb = d.table;
    var wrap = el('div', undefined, 'tablewrap');
    var tbl = el('table');
    wrap.append(tbl);

    if (tb.rows.length === 0) {
      var head = el('tr');
      tb.columns.forEach(function (c, i) { head.append(el('th', columnLabel(tb, i))); });
      var note = el('td', t('noRecords'));
      note.colSpan = Math.max(1, tb.columns.length);
      var noteRow = el('tr');
      noteRow.append(note);
      tbl.append(head, noteRow);
      slot.append(wrap);
      return;
    }

    if (d.resultId) {
      var previewSize = prefs.rows === 10 || prefs.rows === 20 ? prefs.rows : 5;
      var head2 = el('tr');
      if (hasRecordLinks(d)) { head2.append(el('th')); }
      tb.columns.forEach(function (c, i) { head2.append(el('th', columnLabel(tb, i))); });
      tbl.append(head2);
      tb.rows.slice(0, previewSize).forEach(function (row, i) {
        var tr2 = el('tr');
        if (hasRecordLinks(d)) { tr2.append(recordCell(d, i)); }
        row.forEach(function (cell) { tr2.append(el('td', cell)); });
        tbl.append(tr2);
      });
      slot.append(wrap);
      var tblActions = el('div', undefined, 'tblactions');
      var link = el('a', t('openAll', { n: tb.rows.length }), 'btn');
      link.href = base + '/result.html?id=' + encodeURIComponent(d.resultId);
      link.target = '_blank';
      link.rel = 'noopener noreferrer';
      link.onclick = function (e) { openWithHandoff(e, '/result.html?id=' + encodeURIComponent(d.resultId), true); };
      slot.append(tblActions);
      var plan = tableActions(d, false);
      if (plan.resultLink) { tblActions.append(link); }
      if (plan.crmButton) {
        var crm = el('button', t('openInCrm'), 'btn');
        crm.title = t('openInCrmHint');
        crm.onclick = function () {
          openInCrm(d.resultId, crm, undefined, function () { if (!link.isConnected) { tblActions.append(link); } });
        };
        tblActions.append(crm);
      }
      return;
    }

    var search = el('input', undefined, 'txt');
    search.placeholder = t('searchResults');
    stopKeys(search);
    var info = el('span', '', 'muted');
    var pager = el('div', undefined, 'pager');
    slot.append(search, info, wrap, pager);

    var size = 25;
    var page = 0;
    var filtered = tb.rows;

    /** Draws the chart on its canvas. */
    function draw() {
      var pages = Math.max(1, Math.ceil(filtered.length / size));
      if (page >= pages) { page = pages - 1; }
      tbl.replaceChildren();
      var h = el('tr');
      if (hasRecordLinks(d)) { h.append(el('th')); }
      tb.columns.forEach(function (c, i) { h.append(el('th', columnLabel(tb, i))); });
      tbl.append(h);
      filtered.slice(page * size, page * size + size).forEach(function (row) {
        var tr = el('tr');
        if (hasRecordLinks(d)) { tr.append(recordCell(d, tb.rows.indexOf(row))); }
        row.forEach(function (cell) { tr.append(el('td', cell)); });
        tbl.append(tr);
      });
      info.textContent = t('rowsOf', { a: filtered.length, b: tb.rows.length });
      pager.replaceChildren();
      var prev = el('button', t('prev'), 'btn'); prev.disabled = page === 0; prev.onclick = function () { page--; draw(); };
      var next = el('button', t('next'), 'btn'); next.disabled = page >= pages - 1; next.onclick = function () { page++; draw(); };
      pager.append(prev, el('span', t('pageOf', { a: page + 1, b: pages })), next);
    }

    search.oninput = function () {
      var needle = fold(search.value.trim());
      filtered = needle ? tb.rows.filter(function (r) { return r.some(function (c) { return fold(c).indexOf(needle) >= 0; }); }) : tb.rows;
      page = 0;
      draw();
    };
    draw();
  }

  if (document.body) { mount(); }
  else { document.addEventListener('DOMContentLoaded', mount); }

  setInterval(function () { if (document.body) { mount(); updateChip(); } }, 1500);
})();

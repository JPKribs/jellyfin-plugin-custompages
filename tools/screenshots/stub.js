// A stand-in ApiClient with generic sample data for the documentation screenshots.
window.__posts = [];
window.__errors = [];
window.addEventListener('error', function (e) { window.__errors.push(String(e.message)); });
window.addEventListener('unhandledrejection', function (e) { window.__errors.push('rejection: ' + (e.reason && e.reason.message || e.reason)); });

// Draws a simple generic image and returns its PNG as base64.
function drawPng(w, h, paint) {
    var c = document.createElement('canvas');
    c.width = w; c.height = h;
    paint(c.getContext('2d'), w, h);
    return c.toDataURL('image/png').split(',')[1];
}
var LOGO = drawPng(256, 256, function (g, w, h) {
    var grad = g.createLinearGradient(0, 0, w, h);
    grad.addColorStop(0, '#00a4dc'); grad.addColorStop(1, '#7b4fd6');
    g.fillStyle = grad; g.beginPath(); g.arc(128, 128, 120, 0, Math.PI * 2); g.fill();
    g.fillStyle = '#fff'; g.beginPath(); g.moveTo(100, 78); g.lineTo(186, 128); g.lineTo(100, 178); g.closePath(); g.fill();
});
var BANNER = drawPng(640, 240, function (g, w, h) {
    var grad = g.createLinearGradient(0, 0, w, 0);
    grad.addColorStop(0, '#1d3557'); grad.addColorStop(1, '#2a9d8f');
    g.fillStyle = grad; g.fillRect(0, 0, w, h);
    g.fillStyle = 'rgba(255,255,255,0.15)';
    for (var i = 0; i < 8; i++) { g.beginPath(); g.arc(80 * i + 40, 200 - (i % 3) * 40, 60, 0, Math.PI * 2); g.fill(); }
});

var HOUSE_RULES_HTML = [
    '<div class="wrap">',
    '  <h1>House Rules</h1>',
    '  <p class="sub">A few things that keep the server running smoothly for everyone.</p>',
    '  <div class="card"><div class="num">1</div><div><h2>Two streams at a time</h2><p>Each account can play on two devices at once. Sign out of old TVs and phones you no longer use.</p></div></div>',
    '  <div class="card"><div class="num">2</div><div><h2>Prefer direct play</h2><p>Set your app quality to Auto or Maximum. Transcoding uses a lot of server power, especially for 4K.</p></div></div>',
    '  <div class="card"><div class="num">3</div><div><h2>Ask before you share</h2><p>Accounts are for household members. If a friend wants access, send a request and an account will be made for them.</p></div></div>',
    '  <div class="card"><div class="num">4</div><div><h2>Report problems</h2><p>Missing subtitles, wrong artwork, or a file that will not play? Use the Requests page so it gets tracked.</p></div></div>',
    '  <p class="foot">Maintenance window: Sundays from 3 to 4 AM.</p>',
    '</div>'
].join('\n');
var HOUSE_RULES_CSS = [
    'body { margin: 0; background: #101010; color: #f2f2f2; font-family: -apple-system, "Segoe UI", Roboto, Helvetica, Arial, sans-serif; }',
    '.wrap { max-width: 720px; margin: 0 auto; padding: 40px 24px; }',
    'h1 { font-size: 2rem; margin: 0 0 4px; }',
    '.sub { color: rgba(255,255,255,0.6); margin: 0 0 28px; }',
    '.card { display: flex; gap: 16px; background: #1c1c1f; border: 1px solid rgba(255,255,255,0.08); border-radius: 12px; padding: 18px 20px; margin-bottom: 14px; }',
    '.num { flex: none; width: 34px; height: 34px; border-radius: 50%; background: #00a4dc; color: #fff; font-weight: 700; display: flex; align-items: center; justify-content: center; }',
    'h2 { font-size: 1.05rem; margin: 2px 0 6px; }',
    '.card p { margin: 0; color: rgba(255,255,255,0.7); line-height: 1.5; }',
    '.foot { color: rgba(255,255,255,0.5); font-size: 0.9rem; margin-top: 24px; }'
].join('\n');
window.__samples = { HOUSE_RULES_HTML: HOUSE_RULES_HTML, HOUSE_RULES_CSS: HOUSE_RULES_CSS };

window.__config = {
    Pages: [
        { Slug: 'house-rules', Title: 'House Rules', Visibility: 'User', Html: HOUSE_RULES_HTML, Css: HOUSE_RULES_CSS, Js: '', Document: '', SingleFile: false, Unsandboxed: false,
          AllowedUserIds: [], ApiRoutes: [], Enabled: true },
        { Slug: 'requests', Title: 'Requests', Visibility: 'User', Html: '<div class="wrap"></div>', Css: '', Js: '', Document: '', SingleFile: false, Unsandboxed: true,
          AllowedUserIds: [], Enabled: true,
          ApiRoutes: [{ Name: 'downloads', Url: 'http://downloader.local:8080/api/queue', Username: 'jellyfin', Password: 'stored' }] },
        { Slug: 'members-only', Title: 'Members Only', Visibility: 'User', Html: '<h1>Members Only</h1>', Css: '', Js: '', Document: '', SingleFile: false, Unsandboxed: false,
          AllowedUserIds: ['u2', 'u4'], ApiRoutes: [], Enabled: true },
        { Slug: 'request-queue', Title: 'Request Queue', Visibility: 'Admin', Html: '<h1>Queue</h1>', Css: '', Js: '', Document: '', SingleFile: false, Unsandboxed: true,
          AllowedUserIds: [], ApiRoutes: [], Enabled: true },
        { Slug: 'welcome', Title: 'Welcome', Visibility: 'Anonymous', Html: '', Css: '', Js: '', Document: '<!DOCTYPE html><html><body><h1>Welcome</h1></body></html>', SingleFile: true, Unsandboxed: false,
          AllowedUserIds: [], ApiRoutes: [], Enabled: true },
        { Slug: 'holiday-hours', Title: 'Holiday Hours', Visibility: 'Anonymous', Html: '<h1>Holiday Hours</h1>', Css: '', Js: '', Document: '', SingleFile: false, Unsandboxed: false,
          AllowedUserIds: [], ApiRoutes: [], Enabled: false }
    ],
    Assets: [
        { Name: 'logo.png', ContentType: 'image/png', DataBase64: LOGO, Visibility: 'Anonymous' },
        { Name: 'banner.png', ContentType: 'image/png', DataBase64: BANNER, Visibility: 'User' }
    ],
    Stores: [
        { Name: 'queue', Enabled: true, ReadAccess: 'User', ReadScope: 'Own', WriteAccess: 'User', AllowEditOwn: false, MaxRecords: 500, RetentionDays: 90, NotifyOnWrite: true },
        { Name: 'downloads', Enabled: true, ReadAccess: 'Admin', ReadScope: 'All', WriteAccess: 'Admin', AllowEditOwn: false, MaxRecords: 1000, RetentionDays: 30, NotifyOnWrite: false }
    ]
};
var STORE_COUNTS = { queue: 86, downloads: 4 };
function clone(o) { return JSON.parse(JSON.stringify(o)); }
window.ApiClient = {
    serverAddress: function () { return 'http://jellyfin.example.com:8096'; },
    serverId: function () { return 's1'; },
    getCurrentUserId: function () { return 'u1'; },
    accessToken: function () { return 't'; },
    getUrl: function (p, q) { return '/' + p + (q ? '?' + new URLSearchParams(q) : ''); },
    getPluginConfiguration: function () { return Promise.resolve(clone(window.__config)); },
    updatePluginConfiguration: function (id, cfg) {
        window.__posts.push({ url: 'Plugins/' + id + '/Configuration', body: clone(cfg) });
        window.__config = clone(cfg);
        return Promise.resolve({});
    },
    getUsers: function () { return Promise.resolve([
        { Id: 'u1', Name: 'admin', Policy: { IsAdministrator: true } },
        { Id: 'u2', Name: 'alex', Policy: { IsAdministrator: false } },
        { Id: 'u3', Name: 'jordan', Policy: { IsAdministrator: false } },
        { Id: 'u4', Name: 'sam', Policy: { IsAdministrator: false } },
        { Id: 'u5', Name: 'taylor', Policy: { IsAdministrator: false } }]); },
    getUserImageUrl: function () { return ''; },
    getJSON: function (url) { return Promise.resolve({}); },
    getScheduledTasks: function () { return Promise.resolve([]); },
    sendMessage: function () {}, sendWebSocketMessage: function () {},
    ajax: function (o) { return window.ApiClient.fetch(o); },
    fetch: function (o) {
        window.__posts.push({ url: o.url, type: o.type });
        if (/pages\/config\/check/.test(o.url)) return Promise.resolve({ ok: true });
        var m = /store\/([^/]+)\/stats/.exec(o.url);
        if (m) return Promise.resolve({ count: STORE_COUNTS[decodeURIComponent(m[1])] || 0 });
        return Promise.resolve(null);
    }
};
window.Dashboard = { alert: function (m) { window.__errors.push('alert: ' + m); } };

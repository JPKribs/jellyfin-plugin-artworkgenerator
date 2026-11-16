// harness.js
// Screenshot harness. Stubs the Jellyfin globals a config page reads, sends the plugin's own routes to the
// screenshot server (which runs the real plugin code), and loads one page.
window.__log = [];
var COLORS = ['#2f4858', '#6b3a5b', '#1f5f4a', '#5b4a1f', '#3a3f6b', '#5f2f2f', '#2f5f5f', '#4a2f5f'];
var ITEMS = {
    Series: [['5e000000-0000-0000-0000-000000000001', 'Northern Lights', 2019], ['5e000000-0000-0000-0000-000000000002', 'Harbor Town', 2021], ['5e000000-0000-0000-0000-000000000003', 'Hidden Valley', 2016], ['5e000000-0000-0000-0000-000000000004', 'North Ridge', 2023], ['5e000000-0000-0000-0000-000000000005', 'Paper Moon', 2018], ['5e000000-0000-0000-0000-000000000006', 'Silver Coast', 2020]],
    Movie: [['5e000000-0000-0000-0000-000000000011', 'The Long Road', 2014], ['5e000000-0000-0000-0000-000000000012', 'Quiet Harbor', 2022]],
    'MusicVideo,Video': [['5e000000-0000-0000-0000-000000000021', 'Summer Trip', 2024]],
    BoxSet: [['5e000000-0000-0000-0000-000000000031', 'Northern Lights Collection', null]],
    Folder: [['5e000000-0000-0000-0000-000000000041', 'Family Videos', null]]
};
var NAMES = {};
Object.keys(ITEMS).forEach(function (k, ki) {
    ITEMS[k] = ITEMS[k].map(function (r, i) {
        NAMES[r[0]] = { name: r[1], color: COLORS[(ki * 3 + i) % COLORS.length] };
        var item = { Id: r[0], Name: r[1], Type: k === 'MusicVideo,Video' ? 'Video' : k, ImageTags: { Primary: 'tag' } };
        if (r[2]) item.ProductionYear = r[2];
        return item;
    });
});
function posterSvg(id) {
    var n = NAMES[id] || { name: '', color: '#345' };
    return 'data:image/svg+xml,' + encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" width="200" height="300"><defs><linearGradient id="g" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="' + n.color + '"/><stop offset="1" stop-color="#111"/></linearGradient></defs><rect width="100%" height="100%" fill="url(#g)"/><text x="100" y="250" fill="#fff" font-family="Arial" font-weight="bold" font-size="22" text-anchor="middle">' + n.name.split(' ')[0] + '</text></svg>');
}
var configPromise = fetch('/config.json').then(function (r) { return r.json(); });
var logosPromise = fetch('/logos.json').then(function (r) { return r.json(); });
function real(o) {
    var init = { method: o.type || 'GET', headers: { 'Content-Type': o.contentType || 'application/json' } };
    if (o.data !== undefined && init.method !== 'GET') init.body = o.data;
    return fetch(o.url, init).then(function (r) {
        if (!r.ok) return Promise.reject(r);
        return o.dataType === 'json' ? r.json() : r;
    });
}
function route(o) {
    var url = o.url;
    window.__log.push((o.type || 'GET') + ' ' + url);
    if (/\/Logos$/.test(url.split('?')[0])) return (o.type === 'POST') ? new Response(null, { status: 204 }) : logosPromise;
    if (url.indexOf('Fonts/Uploaded') !== -1 && o.type && o.type !== 'GET') return Promise.resolve({ success: true });
    return real(o);
}
window.ApiClient = {
    getUrl: function (p, q) { return '/' + p + (q ? '?' + new URLSearchParams(q).toString() : ''); },
    ajax: function (o) { return Promise.resolve().then(function () { return route(o); }); },
    fetch: function (o) { return Promise.resolve().then(function () { return route(o); }); },
    getJSON: function (u) { return Promise.resolve().then(function () { return route({ url: u, dataType: 'json' }); }); },
    getPluginConfiguration: function () { return configPromise.then(function (c) { return JSON.parse(JSON.stringify(c)); }); },
    updatePluginConfiguration: function (id, c) { window.__saved = c; return Promise.resolve({}); },
    getCurrentUserId: function () { return 'u1'; },
    getItems: function (u, q) { return Promise.resolve({ Items: (ITEMS[q.IncludeItemTypes] || []).slice() }); },
    getVirtualFolders: function () { return Promise.resolve([]); },
    getImageUrl: function (id) { return posterSvg(id); },
    getUsers: function () { return Promise.resolve([]); }
};
window.Dashboard = { alert: function (m) { window.__log.push('ALERT ' + m); } };
window.LibraryMenu = { setTabs: function () {} };
window.addEventListener('error', function (e) { window.__log.push('ERROR ' + e.message); });
window.addEventListener('unhandledrejection', function (e) { window.__log.push('REJECT ' + (e.reason && e.reason.stack || e.reason)); });

var name = new URLSearchParams(location.search).get('page') || 'ag_posters';
fetch('/configurationpage?name=' + name).then(function (r) { return r.text(); }).then(function (html) {
    document.getElementById('host').innerHTML = html;
    var view = document.querySelector('[data-role="page"]');
    return new Promise(function (res) { setTimeout(res, 0); }).then(function () {
        return import('/configurationpage?name=' + name + '.js');
    }).then(function (mod) {
        mod.default(view);
        view.dispatchEvent(new CustomEvent('viewshow'));
        window.__view = view;
        window.__ready = true;
    });
}).catch(function (e) { window.__log.push('LOADFAIL ' + e.stack); });

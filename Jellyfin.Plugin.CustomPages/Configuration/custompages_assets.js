export default function (view) {
    'use strict';

    var PLUGIN_ID = '409ef72d-6014-47fd-8928-ebad581bf81b';
    var TABS = [
        { href: 'configurationpage?name=custompages_pages', name: 'Pages' },
        { href: 'configurationpage?name=custompages_assets', name: 'Assets' }
    ];

    var Shared = null;
    var setTabs = null;
    var _sharedPromise = import('/web/configurationpage?name=custompages_jpkribs_shared.js').then(function (mod) {
        Shared = mod.createShared(view, PLUGIN_ID);
        setTabs = mod.setTabs;
    });

    var MAX_ASSET_BYTES = 5 * 1024 * 1024;

    var config = null;
    var assets = [];
    var _bound = false;

    function el(id) { return view.querySelector('#' + id); }

    function sanitizeAssetName(name) {
        var clean = String(name || '').toLowerCase().replace(/[^a-z0-9._-]+/g, '-').replace(/^[-.]+|[-.]+$/g, '');
        return clean || 'image';
    }

    function readFileAsDataUrl(file) {
        return new Promise(function (resolve, reject) {
            var reader = new FileReader();
            reader.onload = function () { resolve(reader.result); };
            reader.onerror = function () { reject(reader.error); };
            reader.readAsDataURL(file);
        });
    }

    function persistAssets(message) {
        return Shared.getConfig().then(function (fresh) {
            fresh.Assets = assets;
            config = fresh;
            return Shared.saveConfig(fresh);
        }).then(function () {
            renderAssets();
            if (message) Shared.setStatus('assetStatus', message, false);
        }).catch(function () {
            Shared.setStatus('assetStatus', 'Save failed.', true);
        });
    }

    function addAssetFiles(fileList) {
        var files = Array.prototype.slice.call(fileList || []);
        if (!files.length) return;

        var tasks = files.map(function (file) {
            if (file.size > MAX_ASSET_BYTES) {
                Shared.setStatus('assetStatus', file.name + ' is larger than 5 MB and was skipped.', true);
                return Promise.resolve();
            }
            return readFileAsDataUrl(file).then(function (dataUrl) {
                var m = /^data:([^;]+);base64,(.*)$/.exec(dataUrl);
                if (!m) return;
                var name = sanitizeAssetName(file.name);
                var entry = { Name: name, ContentType: m[1], DataBase64: m[2] };
                var existing = assets.findIndex(function (a) { return (a.Name || '').toLowerCase() === name.toLowerCase(); });
                if (existing >= 0) assets[existing] = entry;
                else assets.push(entry);
            });
        });

        Promise.all(tasks).then(function () {
            persistAssets('Uploaded.');
        });
    }

    function copyAssetRef(name) {
        var ref = 'asset/' + name;
        var done = function () { Shared.setStatus('assetStatus', 'Copied ' + ref, false); };
        if (navigator.clipboard && navigator.clipboard.writeText) {
            navigator.clipboard.writeText(ref).then(done).catch(done);
        } else {
            var tmp = document.createElement('textarea');
            tmp.value = ref;
            view.appendChild(tmp);
            tmp.select();
            try { document.execCommand('copy'); } catch (e) { /* ignore */ }
            view.removeChild(tmp);
            done();
        }
    }

    function deleteAsset(name) {
        if (!window.confirm('Delete image "' + name + '"? Pages referencing it will break.')) return;
        assets = assets.filter(function (a) { return a.Name !== name; });
        persistAssets('Deleted.');
    }

    function base64Bytes(b64) {
        if (!b64) return 0;
        var padding = b64.endsWith('==') ? 2 : (b64.endsWith('=') ? 1 : 0);
        return Math.floor(b64.length * 3 / 4) - padding;
    }

    function renderCounts() {
        var images = 0;
        var other = 0;
        var bytes = 0;
        assets.forEach(function (a) {
            if ((a.ContentType || '').indexOf('image/') === 0) images++;
            else other++;
            bytes += base64Bytes(a.DataBase64);
        });
        var size = Shared.formatSize(bytes);
        el('assetCounts').innerHTML =
            '<div class="jpk-card blue"><span class="jpk-card-count">' + images + '</span><span class="jpk-card-label">Images</span></div>' +
            '<div class="jpk-card green"><span class="jpk-card-count">' + other + '</span><span class="jpk-card-label">Other</span></div>' +
            '<div class="jpk-card gray"><span class="jpk-card-count">' + size + '</span><span class="jpk-card-label">Size</span></div>';
    }

    function renderAssets() {
        renderCounts();
        var list = el('assetList');
        Shared.setVisible('assetEmpty', assets.length === 0);
        list.innerHTML = assets.map(function (a) {
            var name = Shared.escapeHtml(a.Name);
            var src = 'data:' + a.ContentType + ';base64,' + a.DataBase64;
            return '<div class="jpk-asset-item">'
                + '<img class="jpk-asset-thumb" src="' + src + '" alt="" />'
                + '<div class="jpk-asset-info">'
                + '<div class="jpk-asset-name">' + name + '</div>'
                + '<div class="jpk-asset-ref">asset/' + name + '</div>'
                + '</div>'
                + '<div class="jpk-asset-actions">'
                + '<button type="button" class="jpk-asset-btn" data-copy="' + name + '" title="Copy path" aria-label="Copy path">'
                + '<span class="material-icons" aria-hidden="true">content_copy</span></button>'
                + '<button type="button" class="jpk-asset-btn danger" data-del="' + name + '" title="Delete" aria-label="Delete">'
                + '<span class="material-icons" aria-hidden="true">delete</span></button>'
                + '</div>'
                + '</div>';
        }).join('');
    }

    function bind() {
        el('assetFile').addEventListener('change', function () {
            addAssetFiles(this.files);
            this.value = '';
        });
        el('assetList').addEventListener('click', function (e) {
            var copy = e.target.closest('[data-copy]');
            if (copy) { copyAssetRef(copy.getAttribute('data-copy')); return; }
            var del = e.target.closest('[data-del]');
            if (del) { deleteAsset(del.getAttribute('data-del')); }
        });
    }

    function load() {
        Shared.getConfig().then(function (cfg) {
            config = cfg;
            assets = config.Assets || [];
            renderAssets();
        });
    }

    view.addEventListener('viewshow', function () {
        _sharedPromise.then(function () {
            setTabs('custompages', 1, TABS);
            if (!_bound) { bind(); _bound = true; }
            load();
        });
    });
}

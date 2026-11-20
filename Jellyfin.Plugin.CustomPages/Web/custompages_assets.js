import {
    createConfigPage, confirmDialog, createFilePicker, createCopyField, loadTranslations, createTranslations, setTabs
} from './configurationpage?name=custompages_jpkribs_shared.js';

export default function (view) {
    'use strict';

    var PLUGIN_ID = '409ef72d-6014-47fd-8928-ebad581bf81b';
    var TAB_INDEX = 1;

    // The strings until the viewer's language loads, so every tr gives the English it is handed.
    var t = createTranslations({});
    var ready = null;

    // tabs
    // Lists the plugin's tabs in the viewer's language. The same in all three page scripts.
    //
    // Output
    // [object[]]: Each tab's href and name.
    function tabs() {
        return [
            { href: 'configurationpage?name=custompages_pages', name: t.tr('TabPages', 'Pages') },
            { href: 'configurationpage?name=custompages_assets', name: t.tr('TabAssets', 'Assets') },
            { href: 'configurationpage?name=custompages_stores', name: t.tr('TabStores', 'Stores') }
        ];
    }

    var Shared = createConfigPage(view, {
        pluginId: PLUGIN_ID,
        tabsKey: 'custompages',
        tabIndex: TAB_INDEX,
        tabs: tabs,
        bind: bind,
        load: load
    }).shared;

    var MAX_ASSET_BYTES = 5 * 1024 * 1024;
    var VISIBILITIES = ['Anonymous', 'User', 'Admin'];

    // visibilityLabel
    // Names a visibility tier in the viewer's language.
    //
    // Inputs
    // v [string]: The tier, Anonymous, User, or Admin.
    //
    // Output
    // [string]: The tier's name.
    function visibilityLabel(v) {
        if (v === 'Admin') return t.tr('TierAdmin');
        if (v === 'User') return t.tr('LabelUser');
        return t.tr('TierAnyone');
    }

    // fillWithCode
    // Writes a translated sentence into a node, with each {0}, {1} placeholder shown as code.
    //
    // Inputs
    // node [HTMLElement]: The node to fill.
    // text [string]: The translated sentence.
    // codes [string[]]: The code shown for each placeholder, in order.
    function fillWithCode(node, text, codes) {
        node.textContent = '';
        String(text).split(/(\{\d+\})/).forEach(function (part) {
            var m = /^\{(\d+)\}$/.exec(part);
            if (m && codes[+m[1]] !== undefined) {
                var code = document.createElement('code');
                code.textContent = codes[+m[1]];
                node.appendChild(code);
            } else if (part) {
                node.appendChild(document.createTextNode(part));
            }
        });
    }

    var assets = [];
    var picker = null;
    // The Copy Fields of the drawn rows, destroyed before the rows are drawn again.
    var rowFields = [];

    function el(id) { return view.querySelector('#' + id); }

    function normalizeVisibility(value) {
        if (typeof value === 'number') return VISIBILITIES[value] || 'Anonymous';
        return VISIBILITIES.indexOf(value) >= 0 ? value : 'Anonymous';
    }

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
            return Shared.saveConfig(fresh);
        }).then(function () {
            renderAssets();
            if (message) Shared.setStatus('assetStatus', message, false);
        }).catch(function () {
            Shared.setStatus('assetStatus', t.tr('ErrorSaveImages'), true);
        });
    }

    // addAssetFiles
    // Reads each picked file and saves it as an asset, replacing one with the same name.
    //
    // Inputs
    // files [File[]]: The files the picker accepted, each already under the size limit.
    // refused [string]: The picker's refusal sentences for the files it turned away, or an empty string.
    function addAssetFiles(files, refused) {
        if (!files.length) return;

        var skipped = [];
        var tasks = files.map(function (file) {
            return readFileAsDataUrl(file).then(function (dataUrl) {
                var m = /^data:([^;]+);base64,(.*)$/.exec(dataUrl);
                if (!m) { skipped.push(file.name); return; }
                var name = sanitizeAssetName(file.name);
                var entry = { Name: name, ContentType: m[1], DataBase64: m[2], Visibility: 'Anonymous' };
                var existing = assets.findIndex(function (a) { return (a.Name || '').toLowerCase() === name.toLowerCase(); });
                if (existing >= 0) {
                    entry.Visibility = normalizeVisibility(assets[existing].Visibility);
                    assets[existing] = entry;
                } else {
                    assets.push(entry);
                }
            }, function () {
                skipped.push(file.name);
            });
        });

        // A skipped file is reported once every read settles, so a later success never hides it.
        Promise.all(tasks).then(function () {
            if (skipped.length === files.length) {
                Shared.setStatus('assetStatus', t.format('ErrorReadFiles', skipped.join(', ')), true);
                return;
            }
            var note = (skipped.length ? ' ' + t.format('ErrorReadFiles', skipped.join(', ')) : '') + (refused ? ' ' + refused : '');
            persistAssets(t.tr('ToastImagesUploaded') + note);
        });
    }

    function deleteAsset(name) {
        confirmDialog(t.format('ConfirmDeleteAsset', name), {
            title: t.tr('DeleteImage'),
            confirmText: t.tr('Delete'),
            destructive: true
        }).then(function (ok) {
            if (!ok) return;
            assets = assets.filter(function (a) { return a.Name !== name; });
            persistAssets(t.tr('ToastImageDeleted'));
        });
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
        Shared.renderCards('assetCounts', [
            { count: images, label: t.tr('Images'), color: 'blue' },
            { count: other, label: t.tr('Other'), color: 'purple' },
            { count: size, label: t.tr('LabelSize'), color: 'gray' }
        ]);
    }

    function make(tag, className, text) {
        var node = document.createElement(tag);
        if (className) node.className = className;
        if (text !== undefined) node.textContent = text;
        return node;
    }

    function rowButton(icon, label, modifier, onClick) {
        var button = make('button', 'base-row-btn' + modifier);
        button.type = 'button';
        button.title = label;
        button.setAttribute('aria-label', label);
        var glyph = make('span', 'material-icons', icon);
        glyph.setAttribute('aria-hidden', 'true');
        button.appendChild(glyph);
        button.addEventListener('click', onClick);
        return button;
    }

    // renderAssetRow
    // Builds one Asset List row: the thumbnail, the name over a Copy Field of its path, the visibility, and Delete.
    //
    // Inputs
    // a [object]: The asset, with Name, ContentType, DataBase64, and Visibility.
    //
    // Output
    // [HTMLElement]: The row.
    function renderAssetRow(a) {
        var row = make('div', 'base-asset-item');

        // Set as a property, never as markup, so a stored value can never break out of the attribute.
        // The halves are also validated server side, since this preview runs against an administrator.
        var thumb = make('img', 'base-asset-thumb');
        thumb.alt = '';
        thumb.src = 'data:' + (a.ContentType || '') + ';base64,' + (a.DataBase64 || '');
        row.appendChild(thumb);

        var info = make('div', 'base-asset-info');
        info.appendChild(make('div', 'base-asset-name', a.Name));
        var path = createCopyField({
            value: 'asset/' + a.Name,
            inline: true,
            size: 'small',
            label: t.tr('LabelPath'),
            copyLabel: t.tr('CopyPath')
        });
        rowFields.push(path);
        info.appendChild(path.element);
        row.appendChild(info);

        var actions = make('div', 'base-asset-actions');
        var select = make('select', 'small');
        select.title = t.tr('LabelVisibility');
        select.setAttribute('aria-label', t.tr('LabelVisibility'));
        var visibility = normalizeVisibility(a.Visibility);
        VISIBILITIES.forEach(function (v) {
            var option = make('option', '', visibilityLabel(v));
            option.value = v;
            option.selected = v === visibility;
            select.appendChild(option);
        });
        select.addEventListener('change', function () {
            setVisibility(a.Name, select.value);
        });
        actions.appendChild(select);

        actions.appendChild(rowButton('delete', t.tr('Delete'), ' destructive', function () { deleteAsset(a.Name); }));

        row.appendChild(actions);
        return row;
    }

    function setVisibility(name, value) {
        var idx = assets.findIndex(function (a) { return a.Name === name; });
        if (idx < 0) return;
        assets[idx].Visibility = normalizeVisibility(value);
        persistAssets(t.tr('ToastSaved'));
    }

    function renderAssets() {
        renderCounts();
        var list = el('assetList');
        Shared.setVisible('assetEmpty', assets.length === 0);
        rowFields.forEach(function (f) { f.destroy(); });
        rowFields = [];
        list.innerHTML = '';
        assets.forEach(function (a) { list.appendChild(renderAssetRow(a)); });
    }

    // bind
    // Loads the viewer's strings, then translates the markup and builds the page in that language.
    function bind() {
        ready = loadTranslations('pages/plugin/translations').then(function (handle) {
            t = handle;
            t.apply(view);
            setTabs('custompages', TAB_INDEX, tabs);
            if (t.has('AssetsIntro')) fillWithCode(el('assetsIntro'), t.tr('AssetsIntro'), ['asset/logo.png']);
            if (t.has('AssetsVisibilityInfo')) fillWithCode(el('assetsInfo'), t.tr('AssetsVisibilityInfo'), ['/pages/asset/{name}']);
            bindPage();
        });
    }

    // bindPage
    // Builds the page's components and wires its controls, once the strings are loaded.
    function bindPage() {
        // Each pick uploads at once, so the picker is emptied after it hands its files over. Emptying it
        // also clears its refusal message, so a refusal that came with good files joins the result.
        var refused = '';
        picker = createFilePicker({
            accept: 'image/*',
            maxSize: MAX_ASSET_BYTES,
            multiple: true,
            dropText: t.tr('DropImages'),
            sizeLimitText: t.tr('ErrorImageTooLarge'),
            onChange: function (files) {
                if (!files.length) return;
                addAssetFiles(files, refused);
                refused = '';
                picker.setValue([]);
            },
            onError: function (messages) {
                // The picker calls onChange in the same turn as onError, so a refusal never outlives its pick.
                refused = messages.join(' ');
                setTimeout(function () { refused = ''; }, 0);
            }
        });
        el('assetPicker').appendChild(picker.element);
    }

    function load() {
        ready.then(function () {
            return Shared.getConfig();
        }).then(function (cfg) {
            assets = cfg.Assets || [];
            renderAssets();
        });
    }
}

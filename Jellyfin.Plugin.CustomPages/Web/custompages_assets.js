import { createConfigPage, confirmDialog, createFilePicker, createCopyField } from './configurationpage?name=custompages_jpkribs_shared.js';

export default function (view) {
    'use strict';

    var PLUGIN_ID = '409ef72d-6014-47fd-8928-ebad581bf81b';
    var TABS = [
        { href: 'configurationpage?name=custompages_pages', name: 'Pages' },
        { href: 'configurationpage?name=custompages_assets', name: 'Assets' },
        { href: 'configurationpage?name=custompages_stores', name: 'Stores' }
    ];

    var Shared = createConfigPage(view, {
        pluginId: PLUGIN_ID,
        tabsKey: 'custompages',
        tabIndex: 1,
        tabs: TABS,
        bind: bind,
        load: load
    }).shared;

    var MAX_ASSET_BYTES = 5 * 1024 * 1024;
    var VISIBILITIES = ['Anonymous', 'User', 'Admin'];
    var VISIBILITY_LABELS = { Anonymous: 'Anyone', User: 'User', Admin: 'Admin' };

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
            Shared.setStatus('assetStatus', 'Could not save the images.', true);
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
                Shared.setStatus('assetStatus', 'Could not read ' + skipped.join(', ') + '.', true);
                return;
            }
            var note = (skipped.length ? ' Could not read ' + skipped.join(', ') + '.' : '') + (refused ? ' ' + refused : '');
            persistAssets('Images uploaded.' + note);
        });
    }

    function deleteAsset(name) {
        confirmDialog('Delete ' + name + '? Pages that use it show a broken image.', { title: 'Delete Image', confirmText: 'Delete', destructive: true }).then(function (ok) {
            if (!ok) return;
            assets = assets.filter(function (a) { return a.Name !== name; });
            persistAssets('Image deleted.');
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
            { count: images, label: 'Images', color: 'blue' },
            { count: other, label: 'Other', color: 'purple' },
            { count: size, label: 'Size', color: 'gray' }
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
        var path = createCopyField({ value: 'asset/' + a.Name, inline: true, size: 'small', label: 'Path', copyLabel: 'Copy Path' });
        rowFields.push(path);
        info.appendChild(path.element);
        row.appendChild(info);

        var actions = make('div', 'base-asset-actions');
        var select = make('select', 'small');
        select.title = 'Visibility';
        select.setAttribute('aria-label', 'Visibility');
        var visibility = normalizeVisibility(a.Visibility);
        VISIBILITIES.forEach(function (v) {
            var option = make('option', '', VISIBILITY_LABELS[v]);
            option.value = v;
            option.selected = v === visibility;
            select.appendChild(option);
        });
        select.addEventListener('change', function () {
            setVisibility(a.Name, select.value);
        });
        actions.appendChild(select);

        actions.appendChild(rowButton('delete', 'Delete', ' destructive', function () { deleteAsset(a.Name); }));

        row.appendChild(actions);
        return row;
    }

    function setVisibility(name, value) {
        var idx = assets.findIndex(function (a) { return a.Name === name; });
        if (idx < 0) return;
        assets[idx].Visibility = normalizeVisibility(value);
        persistAssets('Saved.');
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

    function bind() {
        // Each pick uploads at once, so the picker is emptied after it hands its files over. Emptying it
        // also clears its refusal message, so a refusal that came with good files joins the result.
        var refused = '';
        picker = createFilePicker({
            accept: 'image/*',
            maxSize: MAX_ASSET_BYTES,
            multiple: true,
            dropText: 'Drop images here or click to select.',
            sizeLimitText: '{value} is larger than 5 MB.',
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
        Shared.getConfig().then(function (cfg) {
            assets = cfg.Assets || [];
            renderAssets();
        });
    }
}

import {
    createConfigPage, confirmDialog, createUsageMeter, createCopyField, createSaveBar, loadTranslations, createTranslations, setTabs
} from './configurationpage?name=custompages_jpkribs_shared.js';

export default function (view) {
    'use strict';

    var PLUGIN_ID = '409ef72d-6014-47fd-8928-ebad581bf81b';
    var TAB_INDEX = 2;

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

    // nameRetentionPresets
    // Names each preset retention in days, in the viewer's language.
    function nameRetentionPresets() {
        Array.prototype.forEach.call(el('storeRetention').options, function (option) {
            var days = parseInt(option.value, 10);
            if (!days) return;
            option.textContent = days === 1 ? t.format('RetentionDay', days) : t.format('RetentionDays', days);
        });
    }

    var VISIBILITIES = ['Anonymous', 'User', 'Admin'];
    var SCOPES = ['All', 'Own'];
    var RETENTION_PRESETS = ['0', '1', '7', '30', '90', '180', '365'];
    var RECORD_CEILING = 100000;
    var RETENTION_CEILING = 3650;

    var stores = [];
    var currentIndex = -1;
    var editorEnabled = true;
    var usageMeter = null;
    var urlField = null;
    var saveBar = null;
    // Record counts keyed by the name a store had when it was last saved. A renamed but unsaved store
    // has no server side counterpart, so it reports nothing rather than the previous name's count.
    var counts = {};

    function el(id) { return view.querySelector('#' + id); }

    function slugify(value) {
        return String(value || '')
            .toLowerCase()
            .replace(/[^a-z0-9-_]+/g, '-')
            .replace(/^-+|-+$/g, '');
    }

    function normalizeVisibility(value, fallback) {
        if (typeof value === 'number') return VISIBILITIES[value] || fallback;
        return VISIBILITIES.indexOf(value) >= 0 ? value : fallback;
    }

    function normalizeScope(value) {
        if (typeof value === 'number') return SCOPES[value] || 'All';
        return SCOPES.indexOf(value) >= 0 ? value : 'All';
    }

    function pageOrigin() {
        return ApiClient.serverAddress ? ApiClient.serverAddress() : window.location.origin;
    }

    function newStore() {
        return {
            Name: '',
            Enabled: true,
            ReadAccess: 'Admin',
            ReadScope: 'All',
            WriteAccess: 'User',
            AllowEditOwn: false,
            MaxRecords: 1000,
            RetentionDays: 0,
            NotifyOnWrite: false
        };
    }

    function renderCounts() {
        var total = 0;
        var open = 0;
        stores.forEach(function (s) {
            var c = counts[(s.Name || '').toLowerCase()];
            if (typeof c === 'number') total += c;
            if (normalizeVisibility(s.WriteAccess, 'User') === 'Anonymous' && s.Enabled !== false) open++;
        });
        Shared.renderCards('storeCounts', [
            { count: stores.length, label: t.tr('TabStores'), color: 'blue' },
            { count: total, label: t.tr('CardRecords'), color: 'green' },
            { count: open, label: t.tr('CardOpenToWrites'), color: open > 0 ? 'orange' : 'gray' }
        ]);
    }

    function setEnabledVisual(enabled) {
        var btn = el('btnEnableStore');
        btn.setAttribute('aria-pressed', enabled ? 'true' : 'false');
        var text = enabled ? t.tr('StatusEnabled') : t.tr('Disabled');
        btn.title = text;
        el('enableLabel').textContent = text;
        btn.querySelector('.material-icons').textContent = enabled ? 'visibility' : 'visibility_off';
    }

    function applyWarnings() {
        Shared.setVisible('anonWriteWarning', el('storeWrite').value === 'Anonymous' && editorEnabled);
        Shared.setVisible('retentionCustomRow', el('storeRetention').value === 'custom');

        // Every reader of an Admin tier store is an administrator, and administrators are shown every
        // record, so scope has nothing left to narrow. The control disappears rather than sitting there
        // implying a restriction that would not apply to anyone who can read.
        Shared.setVisible('scopeRow', el('storeRead').value !== 'Admin');

        // With an All records scope, modify access follows read and write alone: anybody who clears both
        // tiers may change or delete any record, and the edit-own toggle never comes into it. That is
        // the existing behaviour stores rely on, so it is surfaced here rather than changed. It only
        // matters when someone other than an administrator clears both tiers.
        var read = el('storeRead').value;
        var write = el('storeWrite').value;
        Shared.setVisible('scopeAllWriteWarning',
            editorEnabled && read !== 'Admin' && write !== 'Admin' && el('storeScope').value === 'All');
    }

    function updateUrl() {
        var name = slugify(el('storeName').value);
        Shared.setVisible('storeUrlRow', !!name);
        urlField.setValue(name ? pageOrigin().replace(/\/+$/, '') + '/pages/store/' + name + '/read' : '');
    }

    function updateUsage() {
        var saved = (stores[currentIndex] && stores[currentIndex].Name || '').toLowerCase();
        var count = counts[saved];
        var limit = parseInt(el('storeMaxRecords').value, 10) || 0;
        // A store that was never saved has no records on the server to measure.
        Shared.setVisible('storeUsageRow', !!saved);
        if (saved && usageMeter) {
            usageMeter.setValue({ used: typeof count === 'number' ? count : 0, total: limit });
        }
    }

    function renderSelect() {
        renderCounts();
        var select = el('selectStore');
        select.innerHTML = stores.map(function (s, i) {
            var name = s.Name || t.tr('StoreOptionNew');
            var label = s.Enabled === false ? t.format('StoreOptionDisabled', name) : name;
            return '<option value="' + i + '">' + Shared.escapeHtml(label) + '</option>';
        }).join('');

        var has = stores.length > 0;
        Shared.setVisible('storeEmpty', !has);
        Shared.setVisible('storeEditor', has);
        Shared.setVisible('btnDeleteStore', has);
        Shared.setVisible('btnEnableStore', has);
        Shared.setVisible('btnClearStore', has);

        if (has) {
            if (currentIndex < 0 || currentIndex >= stores.length) currentIndex = 0;
            select.value = String(currentIndex);
            loadEditor();
        }
    }

    function loadEditor() {
        var s = stores[currentIndex];
        if (!s) return;
        el('storeName').value = s.Name || '';
        el('storeRead').value = normalizeVisibility(s.ReadAccess, 'Admin');
        el('storeScope').value = normalizeScope(s.ReadScope);
        el('storeWrite').value = normalizeVisibility(s.WriteAccess, 'User');
        el('storeEditOwn').checked = !!s.AllowEditOwn;
        el('storeMaxRecords').value = String(s.MaxRecords || 1000);
        el('storeNotify').checked = !!s.NotifyOnWrite;

        var days = String(s.RetentionDays || 0);
        var preset = RETENTION_PRESETS.indexOf(days) >= 0;
        el('storeRetention').value = preset ? days : 'custom';
        el('storeRetentionDays').value = preset ? '' : days;

        editorEnabled = s.Enabled !== false;
        setEnabledVisual(editorEnabled);
        applyWarnings();
        updateUrl();
        updateUsage();
    }

    function readRetention() {
        var choice = el('storeRetention').value;
        if (choice !== 'custom') return parseInt(choice, 10) || 0;
        var days = parseInt(el('storeRetentionDays').value, 10);
        if (!days || days < 1) return 0;
        return Math.min(RETENTION_CEILING, days);
    }

    function readEditorInto(s) {
        s.Name = slugify(el('storeName').value);
        s.Enabled = editorEnabled;
        s.ReadAccess = el('storeRead').value;
        // An Admin tier store shows every reader everything, so a narrowed scope would be a stored
        // value the server never acts on. Save the state the store actually has.
        s.ReadScope = el('storeRead').value === 'Admin' ? 'All' : el('storeScope').value;
        s.WriteAccess = el('storeWrite').value;
        s.AllowEditOwn = el('storeEditOwn').checked;
        s.MaxRecords = Math.min(RECORD_CEILING, Math.max(1, parseInt(el('storeMaxRecords').value, 10) || 1000));
        s.RetentionDays = readRetention();
        s.NotifyOnWrite = el('storeNotify').checked;
    }

    // persist
    // Saves the store list into a fresh copy of the configuration, then redraws and recounts.
    //
    // Output
    // [Promise]: Resolves once saved, or rejects with an Error saying the store could not be saved.
    function persist() {
        return Shared.getConfig().then(function (fresh) {
            fresh.Stores = stores;
            return Shared.saveConfig(fresh);
        }).then(function () {
            renderSelect();
            return loadCounts();
        }, function () {
            throw new Error(t.tr('ErrorSaveStore'));
        });
    }

    // save
    // Checks the editor and saves every store. The Save Bar shows the result.
    //
    // Output
    // [Promise]: Resolves once saved, or rejects with an Error whose message says why not.
    function save() {
        var s = stores[currentIndex];
        if (!s) return Promise.resolve();
        readEditorInto(s);

        if (!s.Name) return Promise.reject(new Error(t.tr('ErrorEnterName')));

        var clash = stores.some(function (other, i) {
            return i !== currentIndex && (other.Name || '').toLowerCase() === s.Name.toLowerCase();
        });
        if (clash) return Promise.reject(new Error(t.tr('ErrorStoreNameTaken')));

        return persist();
    }

    // mountSaveBar
    // Builds the editor's Save Bar. A failed save's Error message is its failed line.
    function mountSaveBar() {
        saveBar = createSaveBar({
            fill: true,
            onSave: save
        });
        el('storeSaveBar').appendChild(saveBar.element);
    }

    function markDirty() {
        saveBar.setDirty(true);
    }

    function clearRecords() {
        var s = stores[currentIndex];
        var name = (s && s.Name || '').toLowerCase();
        if (!name) {
            saveBar.setState('failed', t.tr('ErrorSaveBeforeClear'));
            return;
        }

        var count = counts[name];
        var message = typeof count !== 'number'
            ? t.format('ConfirmClearStoreUnknown', name)
            : t.format(count === 1 ? 'ConfirmClearStoreOne' : 'ConfirmClearStoreMany', name, count);
        confirmDialog(message, {
            title: t.tr('ButtonClearStore'),
            confirmText: t.tr('ButtonClear'),
            destructive: true
        }).then(function (ok) {
            if (!ok) return;
            Shared.apiRequest('pages/store/' + encodeURIComponent(name) + '/clear', 'POST').then(function (result) {
                counts[name] = 0;
                renderCounts();
                updateUsage();
                var removed = (result && result.removed) || 0;
                saveBar.setState('saved', t.format(removed === 1 ? 'ToastRecordRemoved' : 'ToastRecordsRemoved', removed));
            }).catch(function () {
                saveBar.setState('failed', t.tr('ErrorClearStore'));
            });
        });
    }

    function deleteStore() {
        var s = stores[currentIndex];
        if (!s) return;
        var name = (s.Name || '').toLowerCase();
        var count = counts[name];
        var message = typeof count === 'number' && count > 0
            ? t.format('ConfirmDeleteStoreKept', name)
            : t.tr('ConfirmDeleteStore');
        confirmDialog(message, {
            title: t.tr('ButtonDeleteStore'),
            confirmText: t.tr('Delete'),
            destructive: true
        }).then(function (ok) {
            if (!ok) return;
            stores.splice(currentIndex, 1);
            currentIndex = Math.min(currentIndex, stores.length - 1);
            persist().then(function () {
                saveBar.setState('saved', t.tr('ToastStoreDeleted'));
            }, function (err) {
                saveBar.setState('failed', err.message);
            });
        });
    }

    function loadCounts() {
        var live = stores.filter(function (s) { return s.Name; });
        return Promise.all(live.map(function (s) {
            var name = s.Name.toLowerCase();
            return Shared.apiRequest('pages/store/' + encodeURIComponent(name) + '/stats').then(function (stats) {
                if (stats && typeof stats.count === 'number') counts[name] = stats.count;
            }).catch(function () { /* a disabled store, or one not saved yet, answers 404 */ });
        })).then(function () {
            renderCounts();
            updateUsage();
        });
    }

    // bind
    // Loads the viewer's strings, then translates the markup and builds the page in that language.
    function bind() {
        ready = loadTranslations('pages/plugin/translations').then(function (handle) {
            t = handle;
            t.apply(view);
            setTabs('custompages', TAB_INDEX, tabs);
            if (t.has('StoresIntro')) {
                fillWithCode(el('storesIntro'), t.tr('StoresIntro'), ["pageStore.read('name')", "pageStore.write('name', data)"]);
            }
            nameRetentionPresets();
            bindPage();
        });
    }

    // bindPage
    // Builds the page's components and wires its controls, once the strings are loaded.
    function bindPage() {
        usageMeter = createUsageMeter({ label: t.tr('CardRecords'), usedText: t.tr('UsageRecords') });
        el('storeUsage').appendChild(usageMeter.element);
        urlField = createCopyField({
            label: t.tr('LabelUrl'),
            copyLabel: t.tr('CopyUrl')
        });
        urlField.input.id = 'storeUrl';
        el('storeUrlRow').appendChild(urlField.element);
        mountSaveBar();
        // Any edit in the editor, and New and Enabled, leave changes to save.
        el('storeEditor').addEventListener('input', markDirty);
        el('storeEditor').addEventListener('change', markDirty);
        el('selectStore').addEventListener('change', function () {
            readEditorInto(stores[currentIndex] || {});
            currentIndex = parseInt(this.value, 10);
            loadEditor();
        });
        el('btnNewStore').addEventListener('click', function () {
            if (stores[currentIndex]) readEditorInto(stores[currentIndex]);
            stores.push(newStore());
            markDirty();
            currentIndex = stores.length - 1;
            renderSelect();
        });
        el('btnEnableStore').addEventListener('click', function () {
            editorEnabled = !editorEnabled;
            setEnabledVisual(editorEnabled);
            applyWarnings();
            markDirty();
        });
        el('btnClearStore').addEventListener('click', clearRecords);
        el('btnDeleteStore').addEventListener('click', deleteStore);
        el('storeRead').addEventListener('change', applyWarnings);
        el('storeWrite').addEventListener('change', applyWarnings);
        el('storeScope').addEventListener('change', applyWarnings);
        el('storeRetention').addEventListener('change', applyWarnings);
        el('storeName').addEventListener('input', updateUrl);
        el('storeMaxRecords').addEventListener('input', updateUsage);
    }

    function load() {
        ready.then(function () {
            return Shared.getConfig();
        }).then(function (cfg) {
            stores = (cfg.Stores || []).map(function (s) {
                return {
                    Name: s.Name || '',
                    Enabled: s.Enabled !== false,
                    ReadAccess: normalizeVisibility(s.ReadAccess, 'Admin'),
                    ReadScope: normalizeScope(s.ReadScope),
                    WriteAccess: normalizeVisibility(s.WriteAccess, 'User'),
                    AllowEditOwn: !!s.AllowEditOwn,
                    MaxRecords: s.MaxRecords || 1000,
                    RetentionDays: s.RetentionDays || 0,
                    NotifyOnWrite: !!s.NotifyOnWrite
                };
            });
            renderSelect();
            return loadCounts();
        });
    }
}

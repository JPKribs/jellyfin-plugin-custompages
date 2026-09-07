export default function (view) {
    'use strict';

    var PLUGIN_ID = '409ef72d-6014-47fd-8928-ebad581bf81b';
    var TABS = [
        { href: 'configurationpage?name=custompages_pages', name: 'Pages' },
        { href: 'configurationpage?name=custompages_assets', name: 'Assets' },
        { href: 'configurationpage?name=custompages_stores', name: 'Stores' }
    ];

    var Shared = null;
    var setTabs = null;
    var confirmDialog = null;
    var _sharedPromise = import('/web/configurationpage?name=custompages_jpkribs_shared.js').then(function (mod) {
        Shared = mod.createShared(view, PLUGIN_ID);
        setTabs = mod.setTabs;
        confirmDialog = mod.confirmDialog;
    });

    var VISIBILITIES = ['Anonymous', 'User', 'Admin'];
    var SCOPES = ['All', 'Own'];
    var RETENTION_PRESETS = ['0', '1', '7', '30', '90', '180', '365'];
    var RECORD_CEILING = 100000;
    var RETENTION_CEILING = 3650;

    var stores = [];
    var currentIndex = -1;
    var editorEnabled = true;
    // Record counts keyed by the name a store had when it was last saved. A renamed but unsaved store
    // has no server side counterpart, so it reports nothing rather than the previous name's count.
    var counts = {};
    var _bound = false;

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
            { count: stores.length, label: 'Stores', color: 'blue' },
            { count: total, label: 'Records', color: 'green' },
            { count: open, label: 'Open to writes', color: open > 0 ? 'orange' : 'gray' }
        ]);
    }

    function setEnabledVisual(enabled) {
        var btn = el('btnEnableStore');
        btn.classList.toggle('jpk-button-submit', enabled);
        btn.classList.toggle('jpk-secondary', !enabled);
        el('enableLabel').textContent = enabled ? 'Enabled' : 'Disabled';
        btn.querySelector('.material-icons').textContent = enabled ? 'visibility' : 'visibility_off';
    }

    function applyWarnings() {
        Shared.setVisible('anonWriteWarning', el('storeWrite').value === 'Anonymous' && editorEnabled);
        Shared.setVisible('retentionCustomRow', el('storeRetention').value === 'custom');

        // Every reader of an Admin tier store is an administrator, and administrators are shown every
        // record, so scope has nothing left to narrow. The control disappears rather than sitting there
        // implying a restriction that would not apply to anyone who can read.
        Shared.setVisible('scopeRow', el('storeRead').value !== 'Admin');
    }

    function updateUrl() {
        var name = slugify(el('storeName').value);
        var link = el('storeUrl');
        var path = name ? pageOrigin() + '/pages/store/' + name + '/read' : '';
        link.textContent = path || 'Give the store a name.';
        link.href = path || '#';
    }

    function updateUsage() {
        var saved = (stores[currentIndex] && stores[currentIndex].Name || '').toLowerCase();
        var count = counts[saved];
        var limit = parseInt(el('storeMaxRecords').value, 10) || 0;
        if (!saved) {
            el('storeUsage').textContent = 'Not saved yet.';
        } else if (typeof count === 'number') {
            el('storeUsage').textContent = count + ' of ' + limit + ' records stored.';
        } else {
            el('storeUsage').textContent = 'No records yet.';
        }
    }

    function renderSelect() {
        renderCounts();
        var select = el('selectStore');
        select.innerHTML = stores.map(function (s, i) {
            var label = (s.Name || 'new store') + (s.Enabled === false ? ', disabled' : '');
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

    function persist(message) {
        return Shared.getConfig().then(function (fresh) {
            fresh.Stores = stores;
            return Shared.saveConfig(fresh);
        }).then(function () {
            if (message) Shared.setStatus('storeStatus', message, false);
            renderSelect();
            return loadCounts();
        }).catch(function () {
            Shared.setStatus('storeStatus', 'Save failed.', true);
        });
    }

    function save() {
        var s = stores[currentIndex];
        if (!s) return;
        readEditorInto(s);

        if (!s.Name) {
            Shared.setStatus('storeStatus', 'A name is required.', true);
            return;
        }

        var clash = stores.some(function (other, i) {
            return i !== currentIndex && (other.Name || '').toLowerCase() === s.Name.toLowerCase();
        });
        if (clash) {
            Shared.setStatus('storeStatus', 'Another store already uses that name.', true);
            return;
        }

        persist('Saved.');
    }

    function clearRecords() {
        var s = stores[currentIndex];
        var name = (s && s.Name || '').toLowerCase();
        if (!name) {
            Shared.setStatus('storeStatus', 'Save the store before clearing it.', true);
            return;
        }

        var count = counts[name];
        var message = 'Delete every record in "' + name + '"'
            + (typeof count === 'number' ? ' (' + count + ' records)' : '')
            + '? This cannot be undone, and a configuration backup will not bring them back.';
        confirmDialog(message, { title: 'Clear store', confirmText: 'Clear', destructive: true }).then(function (ok) {
            if (!ok) return;
            Shared.apiRequest('pages/store/' + encodeURIComponent(name) + '/clear', 'POST').then(function (result) {
                counts[name] = 0;
                renderCounts();
                updateUsage();
                Shared.setStatus('storeStatus', 'Removed ' + ((result && result.removed) || 0) + ' records.', false);
            }).catch(function () {
                Shared.setStatus('storeStatus', 'Could not clear the store.', true);
            });
        });
    }

    function deleteStore() {
        var s = stores[currentIndex];
        if (!s) return;
        var name = (s.Name || '').toLowerCase();
        var count = counts[name];
        var message = typeof count === 'number' && count > 0
            ? 'Delete "' + name + '"? Its ' + count + ' records stay on disk and come back if you recreate a store with the same name. Clear it first if you want them gone.'
            : 'Delete this store? Pages calling it will stop working.';
        confirmDialog(message, { title: 'Delete store', confirmText: 'Delete', destructive: true }).then(function (ok) {
            if (!ok) return;
            stores.splice(currentIndex, 1);
            currentIndex = Math.min(currentIndex, stores.length - 1);
            persist('Deleted.');
        });
    }

    function copySnippet() {
        var name = slugify(el('storeName').value);
        if (!name) return;
        var snippet = "pageStore.write('" + name + "', { url: value })\n"
            + "  .then(function (record) { console.log(record.id); });\n\n"
            + "pageStore.read('" + name + "')\n"
            + "  .then(function (records) { console.log(records); });";
        Shared.copyToClipboard(snippet).then(function () {
            Shared.setStatus('storeStatus', 'Snippet copied.', false);
        });
    }

    function loadCounts() {
        var live = stores.filter(function (s) { return s.Name; });
        return Promise.all(live.map(function (s) {
            var name = s.Name.toLowerCase();
            return Shared.apiRequest('pages/store/' + encodeURIComponent(name) + '/stats').then(function (stats) {
                if (stats && typeof stats.count === 'number') counts[name] = stats.count;
            }).catch(function () { /* a store that has never been written has no file yet */ });
        })).then(function () {
            renderCounts();
            updateUsage();
        });
    }

    function bind() {
        el('selectStore').addEventListener('change', function () {
            readEditorInto(stores[currentIndex] || {});
            currentIndex = parseInt(this.value, 10);
            loadEditor();
        });
        el('btnNewStore').addEventListener('click', function () {
            if (stores[currentIndex]) readEditorInto(stores[currentIndex]);
            stores.push(newStore());
            currentIndex = stores.length - 1;
            renderSelect();
        });
        el('btnEnableStore').addEventListener('click', function () {
            editorEnabled = !editorEnabled;
            setEnabledVisual(editorEnabled);
            applyWarnings();
        });
        el('btnClearStore').addEventListener('click', clearRecords);
        el('btnDeleteStore').addEventListener('click', deleteStore);
        el('btnCopySnippet').addEventListener('click', copySnippet);
        el('btnSaveStores').addEventListener('click', save);
        el('storeRead').addEventListener('change', applyWarnings);
        el('storeWrite').addEventListener('change', applyWarnings);
        el('storeRetention').addEventListener('change', applyWarnings);
        el('storeName').addEventListener('input', updateUrl);
        el('storeMaxRecords').addEventListener('input', updateUsage);
    }

    function load() {
        Shared.getConfig().then(function (cfg) {
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

    view.addEventListener('viewshow', function () {
        _sharedPromise.then(function () {
            setTabs('custompages', 2, TABS);
            if (!_bound) { bind(); _bound = true; }
            load();
        });
    });
}

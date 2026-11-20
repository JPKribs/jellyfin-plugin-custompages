import {
    createConfigPage, confirmDialog, createUserMultiSelector, createRepeater, createCopyField, createSaveBar, SECRET_KEPT,
    loadTranslations, createTranslations, setTabs
} from './configurationpage?name=custompages_jpkribs_shared.js';

export default function (view) {
    'use strict';

    var PLUGIN_ID = '409ef72d-6014-47fd-8928-ebad581bf81b';
    var TAB_INDEX = 0;

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

    function slugify(value) {
        return String(value || '')
            .toLowerCase()
            .replace(/[^a-z0-9-_]+/g, '-')
            .replace(/^-+|-+$/g, '');
    }

    var VISIBILITIES = ['Anonymous', 'User', 'Admin'];

    var PANE = { html: 'Html', css: 'Css', js: 'Js' };

    // paneHint
    // Gives the hint under the source editor for the pane it shows.
    //
    // Output
    // [string]: The hint in the viewer's language.
    function paneHint() {
        if (editorSingle) return t.tr('HintSingleFile');
        if (currentPane === 'css') return t.tr('HintCss');
        if (currentPane === 'js') return t.tr('HintJs');
        return t.tr('HintHtml');
    }

    var pages = [];
    var currentIndex = -1;
    var currentPane = 'html';
    var editorSingle = false;
    var editorEnabled = true;
    var userPicker = null;
    var routeEditor = null;
    var urlField = null;
    var saveBar = null;

    function el(id) { return view.querySelector('#' + id); }

    function normalizeVisibility(value) {
        if (typeof value === 'number') return VISIBILITIES[value] || 'Anonymous';
        return VISIBILITIES.indexOf(value) >= 0 ? value : 'Anonymous';
    }

    function pageOrigin() {
        return ApiClient.serverAddress ? ApiClient.serverAddress() : window.location.origin;
    }

    function renderCounts() {
        var c = { pub: 0, user: 0, admin: 0, unpublished: 0 };
        pages.forEach(function (p) {
            if (p.Enabled === false) { c.unpublished++; return; }
            var v = normalizeVisibility(p.Visibility);
            if (v === 'User') c.user++;
            else if (v === 'Admin') c.admin++;
            else c.pub++;
        });
        Shared.renderCards('pageCounts', [
            { count: c.pub, label: t.tr('CardPublic'), color: 'green' },
            { count: c.user, label: t.tr('LabelUser'), color: 'blue' },
            { count: c.admin, label: t.tr('TierAdmin'), color: 'purple' },
            { count: c.unpublished, label: t.tr('StatusUnpublished'), color: 'gray' }
        ]);
    }

    function renderSelect() {
        renderCounts();
        var select = el('selectPage');
        select.innerHTML = pages.map(function (p, i) {
            var name = p.Slug || t.tr('PageOptionNew');
            var label = p.Enabled === false ? t.format('PageOptionUnpublished', name) : name;
            return '<option value="' + i + '">' + Shared.escapeHtml(label) + '</option>';
        }).join('');

        var hasPages = pages.length > 0;
        Shared.setVisible('emptyState', !hasPages);
        Shared.setVisible('pageEditor', hasPages);
        Shared.setVisible('btnDeletePage', hasPages);
        Shared.setVisible('btnPublish', hasPages);

        if (hasPages) {
            if (currentIndex < 0 || currentIndex >= pages.length) currentIndex = 0;
            select.value = String(currentIndex);
            loadEditor();
        }
    }

    function setPublishVisual(enabled) {
        var btn = el('btnPublish');
        btn.setAttribute('aria-pressed', enabled ? 'true' : 'false');
        var text = enabled ? t.tr('StatusPublished') : t.tr('StatusUnpublished');
        btn.title = text;
        el('publishLabel').textContent = text;
        btn.querySelector('.material-icons').textContent = enabled ? 'visibility' : 'visibility_off';
    }

    function activeField() {
        return editorSingle ? 'Document' : PANE[currentPane];
    }

    function loadSource() {
        var p = pages[currentIndex];
        if (!p) return;
        el('pageSource').value = p[activeField()] || '';
        el('sourceHint').textContent = paneHint();
    }

    function flushSource() {
        var p = pages[currentIndex];
        if (!p) return;
        p[activeField()] = el('pageSource').value;
    }

    function applyMode() {
        Shared.setVisible('sourceRow', !editorSingle);
        loadSource();
    }

    // An allow list only means something on the User tier. Anyone already serves everybody, and every
    // viewer of an Admin page is an administrator, who is admitted unconditionally. The control
    // disappears on both rather than implying a restriction the server would refuse to save.
    function applyAccess() {
        var restrictable = el('pageVisibility').value === 'User';
        var specific = restrictable && el('pageAccess').value === 'specific';
        Shared.setVisible('accessRow', restrictable);
        Shared.setVisible('allowedUsersRow', specific);
    }

    function mountUserPicker() {
        if (userPicker) return;
        // Administrators are excluded because they always have access. Offering one would let an
        // administrator be unchecked in a list the server ignores for them anyway.
        userPicker = createUserMultiSelector({
            showSelectAll: true,
            adminFilter: 'exclude',
            placeholder: t.tr('Search'),
            selectAllText: t.tr('SelectAll'),
            emptyMessage: t.tr('NoUsersAvailable')
        });
        el('allowedUsers').appendChild(userPicker.element);
    }

    function loadEditor() {
        var p = pages[currentIndex];
        if (!p) return;
        el('pageSlug').value = p.Slug || '';
        el('pageTitle').value = p.Title || '';
        el('pageVisibility').value = normalizeVisibility(p.Visibility);

        editorEnabled = p.Enabled !== false;
        setPublishVisual(editorEnabled);

        var allowed = p.AllowedUserIds || [];
        el('pageAccess').value = allowed.length ? 'specific' : 'all';
        if (userPicker) userPicker.setValue(allowed);
        applyAccess();

        renderRoutes(p.ApiRoutes || []);

        editorSingle = !!p.SingleFile;
        el('pageSingleFile').checked = editorSingle;
        el('pageUnsandboxed').checked = !!p.Unsandboxed;
        applyRoutes();
        currentPane = 'html';
        el('selectSource').value = 'html';
        applyMode();
        updateUrlPreview();
    }

    function mountUrlField() {
        if (urlField) return;
        urlField = createCopyField({
            label: t.tr('LabelUrl'),
            copyLabel: t.tr('CopyUrl'),
            copiedText: t.tr('ToastCopied'),
            failedText: t.tr('ToastCopyFailed')
        });
        urlField.input.id = 'pageUrl';
        el('pageUrlRow').appendChild(urlField.element);
    }

    function updateUrlPreview() {
        var slug = slugify(el('pageSlug').value);
        Shared.setVisible('pageUrlRow', !!slug);
        urlField.setValue(slug ? pageOrigin().replace(/\/+$/, '') + '/pages/' + slug : '');
    }

    function readEditorInto(p) {
        flushSource();
        p.Slug = slugify(el('pageSlug').value);
        p.Title = el('pageTitle').value.trim();
        p.Visibility = el('pageVisibility').value;
        p.AllowedUserIds = readAllowedUsers();
        p.ApiRoutes = readRoutes();
        p.SingleFile = editorSingle;
        p.Unsandboxed = el('pageUnsandboxed').checked;
        p.Enabled = editorEnabled;
    }

    function mountRoutes() {
        if (routeEditor) return;
        routeEditor = createRepeater({
            fields: [
                { key: 'Name', label: t.tr('LabelName'), placeholder: 'name' },
                { key: 'Url', label: t.tr('LabelTargetUrl'), placeholder: 'http://host:port/path', width: '2fr' },
                { key: 'Username', label: t.tr('LabelUsername'), placeholder: t.tr('PlaceholderOptional') },
                {
                    key: 'Password', label: t.tr('LabelPassword'), type: 'secret', placeholder: t.tr('PlaceholderOptional'),
                    clearable: true, clearText: t.tr('ButtonClearPassword')
                }
            ],
            addText: t.tr('ButtonAddRoute'),
            removeText: t.tr('ButtonRemoveNamed'),
            rowName: t.tr('RowRoute'),
            emptyText: t.tr('EmptyRoutes')
        });
        el('routesList').appendChild(routeEditor.element);
    }

    // Never render the stored credential. A stored password shows as SECRET_KEPT, which round-trips
    // untouched, and the server keeps whatever it already has, so the real value never reaches a browser.
    // The password's Clear button empties it, which posts an empty password and removes the stored one.
    function renderRoutes(routes) {
        routeEditor.setValue(routes.map(function (r) {
            return {
                Name: r.Name || '',
                Url: r.Url || '',
                Username: r.Username || '',
                Password: r.Password ? SECRET_KEPT : ''
            };
        }));
    }

    function applyRoutes() {
        Shared.setVisible('routesRow', el('pageUnsandboxed').checked);
    }

    function readRoutes() {
        if (!el('pageUnsandboxed').checked) return [];
        return routeEditor.getValue()
            .map(function (r) {
                return { Name: (r.Name || '').trim(), Url: (r.Url || '').trim(), Username: r.Username || '', Password: r.Password || '' };
            })
            .filter(function (r) { return r.Name || r.Url; });
    }

    // An empty list is the server's "everyone at this tier", so a page is only restricted while the
    // mode is specific and the picker actually holds someone.
    function readAllowedUsers() {
        if (el('pageVisibility').value !== 'User') return [];
        if (el('pageAccess').value !== 'specific') return [];
        return userPicker ? userPicker.getValue() : [];
    }

    // save
    // Checks the editor and saves every page. The Save Bar shows the result.
    //
    // Output
    // [Promise]: Resolves once saved, or rejects with an Error whose message says why not.
    function save() {
        var p = pages[currentIndex];
        if (!p) return Promise.resolve();
        readEditorInto(p);

        if (!p.Slug) return Promise.reject(new Error(t.tr('ErrorEnterSlug')));

        // Saving "only the users I pick" with nobody picked would store an empty list, which the
        // server reads as every user at the tier. Stop rather than quietly widen the page.
        if (el('pageVisibility').value === 'User'
            && el('pageAccess').value === 'specific'
            && !p.AllowedUserIds.length) {
            return Promise.reject(new Error(t.tr('ErrorSelectUser')));
        }

        var clash = pages.some(function (other, i) {
            return i !== currentIndex && (other.Slug || '').toLowerCase() === p.Slug.toLowerCase();
        });
        if (clash) return Promise.reject(new Error(t.tr('ErrorSlugTaken')));

        return persistPages(t.tr('ErrorSavePage'));
    }

    // Jellyfin answers a refused configuration save with a generic error, so the server is asked first
    // whether it would accept this one and why not. That is how a reason such as a renamed route whose
    // password cannot be carried over reaches the administrator. The check is advisory: if it cannot
    // be reached the save goes ahead, and the server still enforces the same rules on the save itself.
    function checkConfig(candidate) {
        return Shared.apiRequest('pages/config/check?language=' + encodeURIComponent(t.language || ''), 'POST', candidate)
            .catch(function () { return null; });
    }

    // persistPages
    // Saves the page list into a fresh copy of the configuration.
    //
    // Inputs
    // errMessage [string]: The reason given when the save fails without one from the server.
    //
    // Output
    // [Promise]: Resolves once saved, or rejects with an Error carrying the server's reason or errMessage.
    function persistPages(errMessage) {
        var refused = null;
        return Shared.getConfig().then(function (fresh) {
            fresh.Pages = pages;
            return checkConfig(fresh).then(function (result) {
                if (result && result.ok === false) {
                    refused = result.message || errMessage;
                    throw new Error(refused);
                }

                return Shared.saveConfig(fresh);
            });
        }).then(function () {
            renderSelect();
        }, function () {
            throw new Error(refused || errMessage);
        });
    }

    // mountSaveBar
    // Builds the editor's Save Bar. A failed save's Error message, such as the server's reason, is its failed line.
    function mountSaveBar() {
        if (saveBar) return;
        saveBar = createSaveBar({
            fill: true,
            onSave: save,
            saveText: t.tr('Save'),
            pendingText: t.tr('SaveBarPending'),
            savingText: t.tr('SaveBarSaving'),
            savedText: t.tr('SaveBarSaved'),
            failedText: t.tr('SaveBarFailed')
        });
        el('pageSaveBar').appendChild(saveBar.element);
    }

    // Edits live in the in-memory page objects until Save persists them, so anything typed into the
    // editor has to be captured before the editor is pointed at a different page.
    function keepCurrentEdits() {
        var current = pages[currentIndex];
        if (current) readEditorInto(current);
    }

    function markDirty() {
        if (saveBar) saveBar.setDirty(true);
    }

    function addPage() {
        keepCurrentEdits();
        markDirty();
        pages.push({ Slug: '', Title: '', Visibility: 'Anonymous', Html: '', Css: '', Js: '', Document: '', SingleFile: false, Unsandboxed: false, AllowedUserIds: [], ApiRoutes: [], Enabled: true });
        currentIndex = pages.length - 1;
        currentPane = 'html';
        editorSingle = false;
        renderSelect();
        el('pageSlug').focus();
    }

    function deletePage() {
        var p = pages[currentIndex];
        if (!p) return;
        var label = p.Slug || t.tr('ConfirmThisPage');
        confirmDialog(t.format('ConfirmDeletePage', label), {
            title: t.tr('ButtonDeletePage'),
            confirmText: t.tr('Delete'),
            cancelText: t.tr('ButtonCancel'),
            destructive: true
        }).then(function (ok) {
            if (!ok) return;
            pages.splice(currentIndex, 1);
            currentIndex = -1;
            persistPages(t.tr('ErrorDeletePage')).then(function () {
                saveBar.setState('saved', t.tr('ToastPageDeleted'));
            }, function (err) {
                saveBar.setState('failed', err.message);
            });
        });
    }

    // bind
    // Loads the viewer's strings, then translates the markup and builds the page in that language.
    function bind() {
        ready = loadTranslations('pages/plugin/translations').then(function (handle) {
            t = handle;
            t.apply(view);
            setTabs('custompages', TAB_INDEX, tabs);
            if (t.has('PagesIntro')) fillWithCode(el('pagesIntro'), t.tr('PagesIntro'), ['/pages/{slug}']);
            if (t.has('RoutesHelp')) fillWithCode(el('routesHelp'), t.tr('RoutesHelp'), ["serverFetch('name', options)"]);
            bindPage();
        });
    }

    // bindPage
    // Builds the page's components and wires its controls, once the strings are loaded.
    function bindPage() {
        mountUserPicker();
        mountUrlField();
        mountRoutes();
        mountSaveBar();
        // Any edit in the editor, and New and Published, leave changes to save.
        // Source only picks which pane shows, so it changes nothing to save.
        function onEdit(e) { if (e.target !== el('selectSource')) markDirty(); }
        el('pageEditor').addEventListener('input', onEdit);
        el('pageEditor').addEventListener('change', onEdit);
        el('selectPage').addEventListener('change', function () {
            keepCurrentEdits();
            currentIndex = parseInt(this.value, 10);
            loadEditor();
        });
        el('pageSingleFile').addEventListener('change', function () {
            flushSource();
            editorSingle = this.checked;
            applyMode();
        });
        el('selectSource').addEventListener('change', function () {
            flushSource();
            currentPane = this.value;
            loadSource();
        });
        el('btnNewPage').addEventListener('click', addPage);
        el('btnDeletePage').addEventListener('click', deletePage);
        el('btnPublish').addEventListener('click', function () {
            editorEnabled = !editorEnabled;
            setPublishVisual(editorEnabled);
            markDirty();
        });
        el('pageVisibility').addEventListener('change', applyAccess);
        el('pageAccess').addEventListener('change', applyAccess);
        el('pageUnsandboxed').addEventListener('change', applyRoutes);
        el('pageSlug').addEventListener('input', updateUrlPreview);
    }

    function load() {
        ready.then(function () {
            return Shared.getConfig();
        }).then(function (cfg) {
            pages = cfg.Pages || [];
            currentIndex = pages.length ? 0 : -1;
            renderSelect();
        });
    }
}

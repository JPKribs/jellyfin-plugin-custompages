import {
    createConfigPage, confirmDialog, createUserMultiSelector, createRepeater, createCheckbox, createCopyField, createSaveBar, SECRET_KEPT
} from './configurationpage?name=custompages_jpkribs_shared.js';

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
        tabIndex: 0,
        tabs: TABS,
        bind: bind,
        load: load
    }).shared;

    function slugify(value) {
        return String(value || '')
            .toLowerCase()
            .replace(/[^a-z0-9-_]+/g, '-')
            .replace(/^-+|-+$/g, '');
    }

    var VISIBILITIES = ['Anonymous', 'User', 'Admin'];

    var PANE = { html: 'Html', css: 'Css', js: 'Js' };
    var PANE_HINT = {
        html: 'HTML that will be combined with CSS and JS into a final page.',
        css: 'Styles that will be added to the final page using a <style> element.',
        js: 'JavaScript that will be added to the final page using a <script> element.'
    };
    var SINGLE_HINT = 'A complete HTML document served exactly as written.';

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
            { count: c.pub, label: 'Public', color: 'blue' },
            { count: c.user, label: 'User', color: 'blue' },
            { count: c.admin, label: 'Admin', color: 'blue' },
            { count: c.unpublished, label: 'Unpublished', color: 'gray' }
        ]);
    }

    function renderSelect() {
        renderCounts();
        var select = el('selectPage');
        select.innerHTML = pages.map(function (p, i) {
            var label = (p.Slug || 'new page') + (p.Enabled === false ? ', unpublished' : '');
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
        btn.title = enabled ? 'Published' : 'Unpublished';
        el('publishLabel').textContent = enabled ? 'Published' : 'Unpublished';
        btn.querySelector('.material-icons').textContent = enabled ? 'visibility' : 'visibility_off';
    }

    function activeField() {
        return editorSingle ? 'Document' : PANE[currentPane];
    }

    function loadSource() {
        var p = pages[currentIndex];
        if (!p) return;
        el('pageSource').value = p[activeField()] || '';
        el('sourceHint').textContent = editorSingle ? SINGLE_HINT : PANE_HINT[currentPane];
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
        userPicker = createUserMultiSelector({ showSelectAll: true, adminFilter: 'exclude' });
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
        urlField = createCopyField({ label: 'URL', copyLabel: 'Copy URL' });
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

    // renderClearControl
    // Draws the Clear box of a route row, which removes the stored password on the next save.
    //
    // Inputs
    // value [boolean|null]: false or true while the route has a stored password, null while it has none.
    // change [function]: Tells the repeater the value changed.
    function renderClearControl(value, change) {
        var stored = value === true || value === false;
        var box = createCheckbox({ ariaLabel: 'Clear Password', field: true, checked: value === true, disabled: !stored, onChange: change });
        return {
            element: box.element,
            input: box.input,
            getValue: function () { return stored ? box.getValue() : null; },
            setValue: function (next) {
                stored = next === true || next === false;
                box.setValue(next === true);
                box.setDisabled(!stored);
            },
            destroy: function () { box.destroy(); }
        };
    }

    function mountRoutes() {
        if (routeEditor) return;
        routeEditor = createRepeater({
            fields: [
                { key: 'Name', label: 'Name', placeholder: 'name' },
                { key: 'Url', label: 'Target URL', placeholder: 'http://host:port/path', width: '2fr' },
                { key: 'Username', label: 'Username', placeholder: 'Optional' },
                { key: 'Password', label: 'Password', type: 'secret', placeholder: 'Optional' },
                { key: 'Clear', label: 'Clear', type: 'custom', width: '4rem', render: renderClearControl }
            ],
            addText: 'Add Route',
            rowName: 'Route',
            emptyText: 'No routes yet. Add one below.'
        });
        el('routesList').appendChild(routeEditor.element);
    }

    // Never render the stored credential. A stored password shows as SECRET_KEPT, which round-trips
    // untouched, and the server keeps whatever it already has, so the real value never reaches a browser.
    function renderRoutes(routes) {
        routeEditor.setValue(routes.map(function (r) {
            return {
                Name: r.Name || '',
                Url: r.Url || '',
                Username: r.Username || '',
                Password: r.Password ? SECRET_KEPT : '',
                Clear: r.Password ? false : null
            };
        }));
    }

    function applyRoutes() {
        Shared.setVisible('routesRow', el('pageUnsandboxed').checked);
    }

    function readRoutes() {
        if (!el('pageUnsandboxed').checked) return [];
        // Clear posts an empty password, which removes the stored one. A typed replacement wins over it.
        return routeEditor.getValue()
            .map(function (r) {
                var password = r.Password || '';
                if (r.Clear === true && password === SECRET_KEPT) password = '';
                return { Name: (r.Name || '').trim(), Url: (r.Url || '').trim(), Username: r.Username || '', Password: password };
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

        if (!p.Slug) return Promise.reject(new Error('Enter a slug.'));

        // Saving "only the users I pick" with nobody picked would store an empty list, which the
        // server reads as every user at the tier. Stop rather than quietly widen the page.
        if (el('pageVisibility').value === 'User'
            && el('pageAccess').value === 'specific'
            && !p.AllowedUserIds.length) {
            return Promise.reject(new Error('Select at least one user, or set Access to All Users.'));
        }

        var clash = pages.some(function (other, i) {
            return i !== currentIndex && (other.Slug || '').toLowerCase() === p.Slug.toLowerCase();
        });
        if (clash) return Promise.reject(new Error('Another page already uses that slug.'));

        return persistPages('Could not save the page.');
    }

    // Jellyfin answers a refused configuration save with a generic error, so the server is asked first
    // whether it would accept this one and why not. That is how a reason such as a renamed route whose
    // password cannot be carried over reaches the administrator. The check is advisory: if it cannot
    // be reached the save goes ahead, and the server still enforces the same rules on the save itself.
    function checkConfig(candidate) {
        return Shared.apiRequest('pages/config/check', 'POST', candidate)
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
    // Builds the editor's Save Bar. The bar in this Base build shows its own failed line, so the reason
    // a save failed is set on it once the bar has handled the rejection.
    function mountSaveBar() {
        if (saveBar) return;
        saveBar = createSaveBar({
            fill: true,
            onSave: function () {
                return save().catch(function (err) {
                    var reason = err && err.message;
                    setTimeout(function () { if (reason && saveBar.getState() === 'failed') saveBar.setState('failed', reason); }, 0);
                    throw err;
                });
            }
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
        var label = p.Slug || 'this page';
        confirmDialog('Delete /pages/' + label + '? This cannot be undone.', { title: 'Delete Page', confirmText: 'Delete', destructive: true }).then(function (ok) {
            if (!ok) return;
            pages.splice(currentIndex, 1);
            currentIndex = -1;
            persistPages('Could not delete the page.').then(function () {
                saveBar.setState('saved', 'Page deleted.');
            }, function (err) {
                saveBar.setState('failed', err.message);
            });
        });
    }

    function bind() {
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
        Shared.getConfig().then(function (cfg) {
            pages = cfg.Pages || [];
            currentIndex = pages.length ? 0 : -1;
            renderSelect();
        });
    }
}

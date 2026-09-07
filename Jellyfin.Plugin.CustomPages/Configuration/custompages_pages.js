export default function (view) {
    'use strict';

    var PLUGIN_ID = '409ef72d-6014-47fd-8928-ebad581bf81b';
    var TABS = [
        { href: 'configurationpage?name=custompages_pages', name: 'Pages' },
        { href: 'configurationpage?name=custompages_assets', name: 'Assets' }
    ];

    var Shared = null;
    var setTabs = null;
    var SECRET_KEPT = '__JPK_SECRET_KEPT__';
    var createUserMultiSelector = null;
    var _sharedPromise = import('/web/configurationpage?name=custompages_jpkribs_shared.js').then(function (mod) {
        Shared = mod.createShared(view, PLUGIN_ID);
        setTabs = mod.setTabs;
        if (mod.SECRET_KEPT) SECRET_KEPT = mod.SECRET_KEPT;
        createUserMultiSelector = mod.createUserMultiSelector;
    });

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

    var config = null;
    var pages = [];
    var currentIndex = -1;
    var currentPane = 'html';
    var editorSingle = false;
    var editorEnabled = true;
    var userPicker = null;
    var routeRows = [];
    var _bound = false;

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
        el('pageCounts').innerHTML =
            '<div class="jpk-card blue"><span class="jpk-card-count">' + c.pub + '</span><span class="jpk-card-label">Public</span></div>' +
            '<div class="jpk-card green"><span class="jpk-card-count">' + c.user + '</span><span class="jpk-card-label">User</span></div>' +
            '<div class="jpk-card purple"><span class="jpk-card-count">' + c.admin + '</span><span class="jpk-card-label">Admin</span></div>' +
            '<div class="jpk-card gray"><span class="jpk-card-count">' + c.unpublished + '</span><span class="jpk-card-label">Unpublished</span></div>';
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
        btn.classList.toggle('jpk-button-submit', enabled);
        btn.classList.toggle('jpk-secondary', !enabled);
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

    // An allow list only means something on a tier that already asks who the viewer is, so the whole
    // control disappears on an anonymous page rather than sitting there implying a restriction the
    // server would refuse to save.
    function applyAccess() {
        var gated = el('pageVisibility').value !== 'Anonymous';
        var specific = gated && el('pageAccess').value === 'specific';
        Shared.setVisible('accessRow', gated);
        Shared.setVisible('allowedUsersRow', specific);
    }

    function mountUserPicker() {
        if (userPicker) return;
        userPicker = createUserMultiSelector({ showSelectAll: true });
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

    function updateUrlPreview() {
        var slug = slugify(el('pageSlug').value);
        var link = el('pageUrl');
        var href = pageOrigin().replace(/\/+$/, '') + '/pages/' + (slug || '');
        link.textContent = '/pages/' + (slug || '');
        link.href = slug ? href : '#';
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

    function makeRouteRow(route) {
        route = route || { Name: '', Url: '', Username: '', Password: '' };
        // Deliberately not jpk-field-row. form.css gives that class a full field gap as an important
        // margin, which stacks a large space between every route. The list container supplies the gap.
        var row = document.createElement('div');
        row.style.display = 'flex';
        row.style.gap = '0.5rem';
        row.style.alignItems = 'center';

        function field(placeholder, value, type) {
            var wrap = document.createElement('div');
            wrap.className = 'jpk-field';
            var inp = document.createElement('input');
            inp.setAttribute('is', 'emby-input');
            inp.type = type || 'text';
            inp.placeholder = placeholder;
            inp.value = value || '';
            inp.autocomplete = 'off';
            wrap.appendChild(inp);
            row.appendChild(wrap);
            return inp;
        }

        var nameEl = field('name', route.Name);
        var urlEl = field('http://host:port/path', route.Url);
        var userEl = field('username (optional)', route.Username);
        // Never render the stored credential. The sentinel round-trips untouched and the server keeps
        // whatever it already has, so the real value is never sent to a browser.
        var passEl = field('password (optional)', route.Password ? SECRET_KEPT : '', 'password');

        var removeWrap = document.createElement('div');
        removeWrap.className = 'jpk-field jpk-field-fixed';
        var remove = document.createElement('button');
        remove.setAttribute('is', 'emby-button');
        remove.type = 'button';
        remove.className = 'raised jpk-icon-btn jpk-button-destructive';
        remove.innerHTML = '<span class="material-icons" aria-hidden="true">delete</span>';
        remove.addEventListener('click', function () {
            var i = routeRows.indexOf(entry);
            if (i >= 0) routeRows.splice(i, 1);
            row.remove();
        });
        removeWrap.appendChild(remove);
        row.appendChild(removeWrap);

        var entry = { row: row, read: function () {
            return { Name: nameEl.value.trim(), Url: urlEl.value.trim(), Username: userEl.value, Password: passEl.value };
        } };
        routeRows.push(entry);
        el('routesList').appendChild(row);
    }

    function renderRoutes(routes) {
        routeRows = [];
        el('routesList').innerHTML = '';
        (routes || []).forEach(makeRouteRow);
    }

    function applyRoutes() {
        Shared.setVisible('routesRow', el('pageUnsandboxed').checked);
    }

    function readRoutes() {
        if (!el('pageUnsandboxed').checked) return [];
        return routeRows
            .map(function (e) { return e.read(); })
            .filter(function (r) { return r.Name || r.Url; });
    }

    // An empty list is the server's "everyone at this tier", so a page is only restricted while the
    // mode is specific and the picker actually holds someone.
    function readAllowedUsers() {
        if (el('pageVisibility').value === 'Anonymous') return [];
        if (el('pageAccess').value !== 'specific') return [];
        return userPicker ? userPicker.getValue() : [];
    }

    function save() {
        var p = pages[currentIndex];
        if (!p) return;
        readEditorInto(p);

        if (!p.Slug) {
            Shared.setStatus('pageStatus', 'A slug is required.', true);
            return;
        }

        // Saving "only the users I pick" with nobody picked would store an empty list, which the
        // server reads as every user at the tier. Stop rather than quietly widen the page.
        if (el('pageVisibility').value !== 'Anonymous'
            && el('pageAccess').value === 'specific'
            && !p.AllowedUserIds.length) {
            Shared.setStatus('pageStatus', 'Pick at least one user, or switch back to All users.', true);
            return;
        }

        var clash = pages.some(function (other, i) {
            return i !== currentIndex && (other.Slug || '').toLowerCase() === p.Slug.toLowerCase();
        });
        if (clash) {
            Shared.setStatus('pageStatus', 'Another page already uses that slug.', true);
            return;
        }

        persistPages('Saved.', 'Save failed.');
    }

    function persistPages(okMessage, errMessage) {
        return Shared.getConfig().then(function (fresh) {
            fresh.Pages = pages;
            config = fresh;
            return Shared.saveConfig(fresh);
        }).then(function () {
            renderSelect();
            Shared.setStatus('pageStatus', okMessage, false);
        }).catch(function () {
            Shared.setStatus('pageStatus', errMessage, true);
        });
    }

    // Edits live in the in-memory page objects until Save persists them, so anything typed into the
    // editor has to be captured before the editor is pointed at a different page.
    function keepCurrentEdits() {
        var current = pages[currentIndex];
        if (current) readEditorInto(current);
    }

    function addPage() {
        keepCurrentEdits();
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
        if (!window.confirm('Delete /pages/' + label + '? This cannot be undone.')) return;

        pages.splice(currentIndex, 1);
        currentIndex = -1;
        persistPages('Deleted.', 'Delete failed.');
    }

    function bind() {
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
        el('btnSavePage').addEventListener('click', save);
        el('btnPublish').addEventListener('click', function () {
            editorEnabled = !editorEnabled;
            setPublishVisual(editorEnabled);
        });
        el('pageVisibility').addEventListener('change', applyAccess);
        el('pageAccess').addEventListener('change', applyAccess);
        el('pageUnsandboxed').addEventListener('change', applyRoutes);
        el('btnAddRoute').addEventListener('click', function () { makeRouteRow(); });
        el('pageSlug').addEventListener('input', updateUrlPreview);
    }

    function load() {
        Shared.getConfig().then(function (cfg) {
            config = cfg;
            pages = config.Pages || [];
            currentIndex = pages.length ? 0 : -1;
            renderSelect();
        });
    }

    view.addEventListener('viewshow', function () {
        _sharedPromise.then(function () {
            setTabs('custompages', 0, TABS);
            mountUserPicker();
            if (!_bound) { bind(); _bound = true; }
            load();
        });
    });
}

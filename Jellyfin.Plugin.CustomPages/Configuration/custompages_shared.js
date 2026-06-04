
export function getTabs() {
    return [
        { href: 'configurationpage?name=custompages_pages', name: 'Pages' },
        { href: 'configurationpage?name=custompages_assets', name: 'Assets' }
    ];
}

export function createShared(view) {
    return {
        pluginId: '409ef72d-6014-47fd-8928-ebad581bf81b',

        escapeHtml: function (str) {
            if (str === null || str === undefined) return '';
            return String(str)
                .replace(/&/g, '&amp;').replace(/</g, '&lt;')
                .replace(/>/g, '&gt;').replace(/"/g, '&quot;');
        },

        slugify: function (str) {
            return String(str || '')
                .toLowerCase()
                .replace(/[^a-z0-9-_]+/g, '-')
                .replace(/^-+|-+$/g, '');
        },

        getConfig: function () {
            var self = this;
            return new Promise(function (resolve, reject) {
                ApiClient.getPluginConfiguration(self.pluginId).then(resolve).catch(reject);
            });
        },

        saveConfig: function (config) {
            var self = this;
            return new Promise(function (resolve, reject) {
                ApiClient.updatePluginConfiguration(self.pluginId, config).then(resolve).catch(reject);
            });
        },

        setVisible: function (id, visible) {
            var el = typeof id === 'string' ? view.querySelector('#' + id) : id;
            if (el) {
                if (visible) el.classList.remove('hidden');
                else el.classList.add('hidden');
            }
        },

        setStatus: function (elementId, message, isError) {
            var el = view.querySelector('#' + elementId);
            if (el) {
                el.textContent = message;
                el.style.color = isError ? 'var(--cp-error-color)' : 'var(--cp-success-color)';
                if (message) {
                    setTimeout(function () { if (el.textContent === message) el.textContent = ''; }, 5000);
                }
            }
        }
    };
}

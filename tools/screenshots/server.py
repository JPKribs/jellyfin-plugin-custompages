"""Custom Pages screenshot server: serves the plugin's tabs, its served pages, Base and jellyfin-web the way Jellyfin does.

Usage: python3 -I server.py 8811
The tabs get the plugin's US English strings. Base comes from the NuGet package folder at the version the plugin's csproj names. JELLYFIN_WEB picks the jellyfin-web folder.
"""
import http.server, importlib.util, json, os, re, sys, urllib.parse

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.join(HERE, '..', '..', 'Jellyfin.Plugin.CustomPages')
JF = os.environ.get('JELLYFIN_WEB', '/Applications/Jellyfin.app/Contents/Resources/jellyfin-web')
TYPES = {'.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.html': 'text/html', '.svg': 'image/svg+xml',
         '.woff2': 'font/woff2', '.woff': 'font/woff', '.ttf': 'font/ttf', '.png': 'image/png'}


def base_folder():
    """The Base package in the NuGet folder, at the version the plugin's csproj references."""
    csproj = open(os.path.join(REPO, 'Jellyfin.Plugin.CustomPages.csproj'), encoding='utf-8').read()
    match = re.search(r'Include="JPKribs\.Jellyfin\.Base"\s+Version="([^"]+)"', csproj)
    if not match:
        sys.exit('The csproj has no JPKribs.Jellyfin.Base PackageReference.')
    packages = os.environ.get('NUGET_PACKAGES', os.path.expanduser('~/.nuget/packages'))
    folder = os.path.join(packages, 'jpkribs.jellyfin.base', match.group(1))
    if not os.path.isdir(folder):
        sys.exit('Base %s is not in %s. Run dotnet restore on the plugin first.' % (match.group(1), packages))
    return folder


BASE = base_folder()


def strings():
    """The US English strings as the plugin serves them: its declared Jellyfin strings, then Localization/en-us.json."""
    csproj = open(os.path.join(REPO, 'Jellyfin.Plugin.CustomPages.csproj'), encoding='utf-8').read()
    jellyfin = {src: json.load(open(os.path.join(BASE, 'jellyfin', src, 'en-us.json'), encoding='utf-8')) for src in ('web', 'server')}
    found = {}
    for m in re.finditer(r'<JellyfinString Include="([^"]+)"(?:\s+Source="(\w+)")?', csproj):
        for key in m.group(1).split(';'):
            found[key] = jellyfin[m.group(2) or 'web'].get(key, key)
    found.update(json.load(open(os.path.join(REPO, 'Localization', 'en-us.json'), encoding='utf-8')))
    return found


STRINGS = strings()
if not os.path.isfile(os.path.join(JF, 'index.html')):
    sys.exit('No jellyfin-web at %s. Set JELLYFIN_WEB to a jellyfin-web folder.' % JF)

# Loaded by path, since python3 -I leaves this folder off the import path, and with no bytecode left behind.
sys.dont_write_bytecode = True
_spec = importlib.util.spec_from_file_location('public_pages', os.path.join(HERE, 'public_pages.py'))
PUBLIC = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(PUBLIC)
PUBLIC.configure(os.path.join(REPO, 'Web', 'Templates'), os.path.join(BASE, 'templates', 'status.html'), STRINGS)


def stylesheets():
    """jellyfin-web's stylesheets in load order: index.html's two first, the dark theme, then every chunk. A chunk that
    sets the root font to anything but the dashboard's 93% belongs to another layout, such as TV, and is left out."""
    first = re.findall(r'href="([^"?]+\.css)', open(os.path.join(JF, 'index.html')).read())
    rest = []
    for name in sorted(os.listdir(JF)):
        if not name.endswith('.css') or name in first:
            continue
        sizes = re.findall(r'html\{[^}]*?font-size:([^;}!]+)', open(os.path.join(JF, name), encoding='utf-8', errors='ignore').read())
        if any(size.strip() != '93%' for size in sizes):
            continue
        rest.append(name)
    return first + ['themes/dark/theme.css'] + rest


class H(http.server.BaseHTTPRequestHandler):
    def log_message(self, *a):
        pass

    def send(self, status, data=b'', kind=None):
        self.send_response(status)
        if kind:
            self.send_header('Content-Type', kind)
        self.send_header('Cache-Control', 'no-store')
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self):
        u = urllib.parse.urlparse(self.path)
        q = urllib.parse.parse_qs(u.query)
        if u.path in ('/', '/harness.html'):
            # jellyfin-web's stylesheets go in as plain links, in load order, so they apply before the tab mounts.
            links = ''.join('<link rel="stylesheet" href="/jf/%s">\n' % name for name in stylesheets())
            page = open(os.path.join(HERE, 'harness.html'), encoding='utf-8').read().replace('<!-- jellyfin-web stylesheets -->\n', links)
            return self.send(200, page.encode('utf-8'), 'text/html')
        if u.path == '/pages/plugin/translations':
            return self.send(200, json.dumps(STRINGS).encode('utf-8'), 'application/json')
        if u.path == '/System/Info/Public':
            return self.send(200, b'{"Id":"s1"}', 'application/json')
        m = re.match(r'^/pages/([a-z0-9_-]+)(?:/(user|admin))?$', u.path)
        if m:
            # The shell's content fetch is answered as a signed in user. A slug with no content answers 403.
            if m.group(2) and not self.headers.get('Authorization'):
                return self.send(401)
            page = PUBLIC.render(m.group(1), 'user' if m.group(2) else 'shell')
            if page is None:
                return self.send(403 if m.group(2) else 404)
            return self.send(200, page.encode('utf-8'), 'text/html; charset=utf-8')
        if u.path.endswith('/configurationpage'):
            n = q['name'][0]
            if 'jpkribs_shared' in n:
                path = os.path.join(BASE, 'assets', 'jpkribs_shared' + os.path.splitext(n)[1])
            elif os.path.splitext(n)[1] in ('.js', '.css'):
                path = os.path.join(REPO, 'Web', n)
            else:
                path = os.path.join(REPO, 'Web', n + '.html')
        elif u.path.startswith('/jf/'):
            path = os.path.join(JF, u.path[4:])
        elif u.path.startswith('/web/'):
            path = os.path.join(JF, u.path[5:])
        else:
            path = os.path.join(HERE, u.path.lstrip('/'))
        if not os.path.isfile(path):
            return self.send(404)
        self.send(200, open(path, 'rb').read(), TYPES.get(os.path.splitext(path)[1], 'application/octet-stream'))


http.server.ThreadingHTTPServer(('127.0.0.1', int(sys.argv[1])), H).serve_forever()

"""Renders the served pages from the repository's templates and Base's status template, with sample content.

server.py loads this file and calls render() for each request, so the pages always match the templates on disk.
"""
import html, os, re

REPO = None
STATUS = None

def fill(text, values):
    return re.sub(r'\{\{([A-Z_]+)\}\}', lambda m: values.get(m.group(1), m.group(0)), text)

def read(p): return open(p, encoding='utf-8').read()

def status(heading, message, title=None, button_text='Back to Jellyfin', button_href='../web/', spinner=False, content=''):
    sp = '<div class="base-spinner" role="status" aria-label="Loading"></div>' if spinner else ''
    btn = '' if not button_text or not button_href else '<a class="base-btn" href="%s">%s</a>' % (html.escape(button_href), html.escape(button_text))
    return fill(read(STATUS), {'TITLE': html.escape(title or heading), 'HEADING': html.escape(heading), 'MESSAGE': html.escape(message),
                               'SPINNER': sp, 'BUTTON': btn, 'CONTENT': content})

def configure(templates, status_template):
    """Points the renderer at the plugin's Web/Templates folder and Base's status.html."""
    global REPO, STATUS
    REPO, STATUS = templates, status_template

def shell(slug, tier):
    content = fill(read(os.path.join(REPO, 'custompages_shell.html')), {'SLUG': slug, 'TIER': tier})
    return status('Loading…', '', button_text=None, spinner=True, content=content)

def inner(title, css, body, js):
    return fill(read(os.path.join(REPO, 'custompages_inner.html')), {'TITLE': html.escape(title), 'CSS': css, 'BODY': body, 'JS': js})

SANDBOX = ' sandbox="allow-scripts allow-forms allow-popups allow-popups-to-escape-sandbox"'
def wrapper(title, doc, sandboxed=True):
    return fill(read(os.path.join(REPO, 'custompages_wrapper.html')), {'TITLE': html.escape(title), 'SANDBOX': SANDBOX if sandboxed else '', 'SRCDOC': html.escape(doc)})

FONT = '-apple-system, "Segoe UI", Roboto, Helvetica, Arial, sans-serif'

HOUSE_HTML = '''<div class="wrap">
  <h1>House Rules</h1>
  <p class="sub">A few things that keep the server running smoothly for everyone.</p>
  <div class="card"><div class="num">1</div><div><h2>Two streams at a time</h2><p>Each account can play on two devices at once. Sign out of old TVs and phones you no longer use.</p></div></div>
  <div class="card"><div class="num">2</div><div><h2>Prefer direct play</h2><p>Set your app quality to Auto or Maximum. Transcoding uses a lot of server power, especially for 4K.</p></div></div>
  <div class="card"><div class="num">3</div><div><h2>Ask before you share</h2><p>Accounts are for household members. If a friend wants access, send a request and an account will be made for them.</p></div></div>
  <div class="card"><div class="num">4</div><div><h2>Report problems</h2><p>Missing subtitles, wrong artwork, or a file that will not play? Use the Requests page so it gets tracked.</p></div></div>
  <p class="foot">Maintenance window: Sundays from 3 to 4 AM.</p>
</div>'''
HOUSE_CSS = '''body { margin: 0; background: #101010; color: #f2f2f2; font-family: %s; }
.wrap { max-width: 720px; margin: 0 auto; padding: 40px 24px; }
h1 { font-size: 2rem; margin: 0 0 4px; }
.sub { color: rgba(255,255,255,0.6); margin: 0 0 28px; }
.card { display: flex; gap: 16px; background: #1c1c1f; border: 1px solid rgba(255,255,255,0.08); border-radius: 12px; padding: 18px 20px; margin-bottom: 14px; }
.num { flex: none; width: 34px; height: 34px; border-radius: 50%%; background: #00a4dc; color: #fff; font-weight: 700; display: flex; align-items: center; justify-content: center; }
h2 { font-size: 1.05rem; margin: 2px 0 6px; }
.card p { margin: 0; color: rgba(255,255,255,0.7); line-height: 1.5; }
.foot { color: rgba(255,255,255,0.5); font-size: 0.9rem; margin-top: 24px; }''' % FONT

REQ_HTML = '''<div class="wrap">
  <h1>Requests</h1>
  <p class="sub">Ask for a movie or show and follow its progress here.</p>
  <form id="form" class="panel">
    <label for="title">Title</label>
    <input id="title" placeholder="Title and year" autocomplete="off">
    <label for="type">Type</label>
    <select id="type"><option>Movie</option><option>Show</option></select>
    <button type="submit">Send request</button>
  </form>
  <h2>Your requests</h2>
  <div id="list"></div>
</div>'''
REQ_CSS = '''body { margin: 0; background: #101010; color: #f2f2f2; font-family: %s; }
.wrap { max-width: 720px; margin: 0 auto; padding: 40px 24px; }
h1 { font-size: 2rem; margin: 0 0 4px; }
.sub { color: rgba(255,255,255,0.6); margin: 0 0 24px; }
.panel { background: #1c1c1f; border: 1px solid rgba(255,255,255,0.08); border-radius: 12px; padding: 20px; }
label { display: block; font-size: 0.85rem; color: rgba(255,255,255,0.7); margin: 0 0 6px; }
input, select { box-sizing: border-box; width: 100%%; padding: 10px 12px; margin: 0 0 14px; background: #252528; color: #f2f2f2; border: 1px solid rgba(255,255,255,0.12); border-radius: 8px; font: inherit; }
button { padding: 10px 20px; border: 0; border-radius: 8px; background: #00a4dc; color: #fff; font: inherit; font-weight: 600; cursor: pointer; }
h2 { font-size: 1.1rem; margin: 28px 0 12px; }
.row { display: flex; align-items: center; justify-content: space-between; gap: 12px; background: #1c1c1f; border: 1px solid rgba(255,255,255,0.08); border-radius: 10px; padding: 12px 16px; margin-bottom: 8px; }
.name { font-size: 1rem; }
.meta { color: rgba(255,255,255,0.5); font-size: 0.85rem; margin-top: 2px; }
.badge { flex: none; font-size: 0.8rem; padding: 4px 10px; border-radius: 999px; }
.badge.queued { background: rgba(240,173,78,0.15); color: #f0ad4e; }
.badge.downloading { background: rgba(0,164,220,0.15); color: #4fc3f7; }
.badge.done { background: rgba(92,184,92,0.15); color: #7bd17b; }''' % FONT
REQ_JS = '''function render(records) {
  document.getElementById('list').innerHTML = records.map(function (r) {
    var d = new Date(r.createdUtc).toLocaleDateString('en-US');
    return '<div class="row"><div><div class="name">' + r.data.title + '</div><div class="meta">' + r.data.type + ', ' + d + '</div></div>'
      + '<span class="badge ' + r.data.status + '">' + r.data.status + '</span></div>';
  }).join('');
}
document.getElementById('form').addEventListener('submit', function (e) {
  e.preventDefault();
  var title = document.getElementById('title').value.trim();
  if (!title) return;
  pageStore.write('queue', { title: title, type: document.getElementById('type').value, status: 'queued' }, { label: title })
    .then(function () { return pageStore.read('queue'); }).then(render);
});
pageStore.read('queue').then(render);'''
# Stands in for the helper the server injects into unsandboxed pages, answering with sample records.
STORE_STUB = '''var pageStore = { read: function () { return Promise.resolve([
  { id: 'a1', createdUtc: '2026-11-14T18:20:00Z', data: { title: 'Metropolis (1927)', type: 'Movie', status: 'queued' } },
  { id: 'a2', createdUtc: '2026-11-12T20:05:00Z', data: { title: 'Night of the Living Dead (1968)', type: 'Movie', status: 'downloading' } },
  { id: 'a3', createdUtc: '2026-11-09T16:40:00Z', data: { title: 'Nosferatu (1922)', type: 'Movie', status: 'done' } }
]); }, write: function () { return Promise.resolve({}); } };
'''

# Each slug's tier and its content. members-only has no content, since the viewer is not on its list and the content endpoint answers 403.
PAGES = {
    'house-rules': ('User', lambda: wrapper('House Rules', inner('House Rules', HOUSE_CSS, HOUSE_HTML, ''), sandboxed=True)),
    'requests': ('User', lambda: inner('Requests', REQ_CSS, REQ_HTML, STORE_STUB + REQ_JS)),
    'members-only': ('User', None),
}

def render(slug, part):
    """The shell for a slug, or its content for part 'user'. None when the slug or its content does not exist."""
    if slug not in PAGES:
        return None
    tier, content = PAGES[slug]
    if part == 'shell':
        return shell(slug, tier.lower())
    return content() if content else None

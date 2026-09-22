// Messages between the screens and DesktopGamepad.exe (WebView2 web messages).
// Opened in a normal browser (npm run dev), a stand-in keeps settings in localStorage so the screens still work.

const webview = typeof window !== 'undefined' && window.chrome && window.chrome.webview;
const listeners = new Set();

export const inApp = !!webview;

export function onMessage(fn) {
  listeners.add(fn);
  return () => listeners.delete(fn);
}

function deliver(msg) {
  listeners.forEach((fn) => fn(msg));
}

if (webview) {
  webview.addEventListener('message', (e) => deliver(e.data));
}

export function send(msg) {
  if (webview) webview.postMessage(msg);
  else devHost(msg);
}

// ---- Stand-in host for the browser preview ----

const DEV_KEY = 'desktopgamepad.dev.settings';

function devDefaults() {
  const ids = ['lt', 'lb', 'rt', 'rb', 'lstick', 'lclick', 'rstick', 'rclick', 'dpadUp', 'dpadDown', 'dpadLeft', 'dpadRight', 'view', 'menu', 'y', 'x', 'b', 'a'];
  const seed = () => {
    const m = {};
    ids.forEach((id) => { m[id] = { base: {}, layer: {} }; });
    const set = (id, b, l) => { if (b) m[id].base.tap = b; if (l) m[id].layer.tap = l; };
    set('lstick', 'Move cursor'); set('rstick', 'Scroll'); set('a', 'Left click'); set('view', 'Right click');
    set('rclick', 'Middle click'); set('b', 'Esc', 'Ctrl+W'); set('y', 'F'); set('lt', 'Precision cursor');
    set('rt', 'Hold for second layer'); set('dpadUp', 'Volume up'); set('dpadDown', 'Volume down');
    set('dpadLeft', 'Left arrow', 'Ctrl+Alt+Left'); set('dpadRight', 'Right arrow', 'Ctrl+Alt+Right');
    return m;
  };
  const browser = seed();
  browser.lb.base.tap = 'Ctrl+Shift+Tab'; browser.rb.base.tap = 'Ctrl+Tab'; browser.menu.base.tap = 'Right click'; browser.view.base.tap = 'Ctrl+R'; browser.view.layer.tap = 'F11';
  return {
    version: 1, firstRunDone: false, startWithWindows: true, mouseMode: 'on', keyboard: true,
    cursorSpeed: 7, scrollSpeed: 8, activeProfile: 'Desktop', profileOrder: ['Desktop', 'Browser', 'Media'],
    profiles: { Desktop: seed(), Browser: browser, Media: seed() },
    apps: [
      { id: 'browser', kind: 'browser', rule: 'Browser' },
      { id: 'player', kind: 'player', rule: 'Media' },
      { id: 'xbox', kind: 'xbox', name: 'Xbox', path: 'Microsoft.GamingApp', rule: 'off' },
      { id: 'steam', kind: 'steam', name: 'Steam', path: 'C:\\Program Files (x86)\\Steam\\steam.exe', rule: 'Desktop' }
    ],
    notify: { battery: true, sound: true }
  };
}

function devHost(msg) {
  const reply = (m) => setTimeout(() => deliver(m), 30);
  if (msg.type === 'ready') {
    let settings = null;
    try { settings = JSON.parse(localStorage.getItem(DEV_KEY) || 'null'); } catch (e) {}
    reply({
      type: 'state',
      settings: settings || devDefaults(),
      system: {
        defaultBrowser: { name: 'Google Chrome', path: 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe' },
        defaultPlayer: { name: 'VLC media player', path: 'C:\\Program Files\\VideoLAN\\VLC\\vlc.exe' }
      },
      status: { controller: { connected: true, name: 'Xbox Wireless Controller', connection: 'Bluetooth', battery: 83, kind: 'xbox' }, steamConflict: false, adminWindow: false }
    });
  } else if (msg.type === 'saveSettings') {
    try { localStorage.setItem(DEV_KEY, JSON.stringify(msg.settings)); } catch (e) {}
  } else if (msg.type === 'resetProfiles') {
    let settings = null;
    try { settings = JSON.parse(localStorage.getItem(DEV_KEY) || 'null'); } catch (e) {}
    const fresh = devDefaults();
    const s = settings || fresh;
    s.profiles = fresh.profiles; s.profileOrder = fresh.profileOrder; s.activeProfile = fresh.activeProfile;
    s.apps.forEach((a) => { if (a.rule !== 'off') a.rule = a.kind === 'browser' ? 'Browser' : a.kind === 'player' ? 'Media' : 'Desktop'; });
    try { localStorage.setItem(DEV_KEY, JSON.stringify(s)); } catch (e) {}
    devHost({ type: 'ready' });
  } else if (msg.type === 'pickApp') {
    reply({ type: 'appPicked', name: 'Spotify', path: 'C:\\Users\\You\\AppData\\Roaming\\Spotify\\Spotify.exe' });
  }
}

import React, { useCallback, useEffect, useRef, useState } from 'react';
import { onMessage, send } from './bridge.js';
import { handleNav, navBlocked } from './padnav.js';
import { Dialog } from './common.jsx';
import Sidebar, { PAGE_ORDER } from './Sidebar.jsx';
import Home from './Home.jsx';
import Keymapping from './Keymapping.jsx';
import Apps from './Apps.jsx';
import SettingsPage from './SettingsPage.jsx';
import Setup from './Setup.jsx';

export const APP_NAME = 'Desktop Gamepad';

export default function App() {
  const [settings, setSettings] = useState(null);
  const [system, setSystem] = useState({});
  const [status, setStatus] = useState({ controller: null, steamConflict: false, adminWindow: false });
  const [page, setPage] = useState('home');
  const [inSetup, setInSetup] = useState(false);
  const [dialog, setDialog] = useState(null);
  const [lastInput, setLastInput] = useState(null);
  // Controller inputs held right now, so Keymapping can show the second layer while its button is held.
  const [heldInputs, setHeldInputs] = useState([]);
  // The profile open on the Keymapping page. Separate from settings.activeProfile, the profile in use: editing a
  // profile must not change what the controller does in other apps.
  const [editProfile, setEditProfile] = useState(null);
  const appPicked = useRef(null);
  const setupDecided = useRef(false);
  const navRef = useRef(null);

  useEffect(() => {
    const off = onMessage((m) => {
      if (m.type === 'state') {
        setSettings(m.settings);
        setSystem(m.system || {});
        if (m.status) setStatus(m.status);
        if (!setupDecided.current) { setupDecided.current = true; setInSetup(!m.settings.firstRunDone); }
      } else if (m.type === 'status') {
        setStatus(m.status);
      } else if (m.type === 'input') {
        setLastInput({ id: m.id, at: Date.now() });
        setHeldInputs((held) => (held.includes(m.id) ? held : held.concat(m.id)));
      } else if (m.type === 'inputUp') {
        setHeldInputs((held) => held.filter((id) => id !== m.id));
      } else if (m.type === 'nav') {
        if (navRef.current) navRef.current(m.key);
      } else if (m.type === 'appPicked') {
        if (appPicked.current) appPicked.current(m);
      }
    });
    send({ type: 'ready' });
    return off;
  }, []);

  /** Change settings with a function on a copy; the result is shown at once and saved by the app. */
  const update = useCallback((change) => {
    setSettings((prev) => {
      const next = structuredClone(prev);
      change(next);
      send({ type: 'saveSettings', settings: next });
      return next;
    });
  }, []);

  if (!settings) return <div style={{ height: '100%', background: '#202020' }} />;

  const kind = (status.controller && status.controller.kind) || 'xbox';
  const shared = { settings, update, system, status, kind, setPage, setDialog, editProfile, setEditProfile, heldInputs, appName: APP_NAME };

  // The controller in this window: LB / RB switch sections, everything else moves the highlight (padnav.js).
  navRef.current = (key) => {
    if (key === 'prevPage' || key === 'nextPage') {
      if (inSetup || dialog || navBlocked()) return;
      const i = PAGE_ORDER.indexOf(page);
      setPage(PAGE_ORDER[(i + (key === 'nextPage' ? 1 : PAGE_ORDER.length - 1)) % PAGE_ORDER.length]);
      return;
    }
    handleNav(key);
  };

  return (
    <div style={{ height: '100%', display: 'flex', flexDirection: 'column', background: '#202020' }}>
      {inSetup ? (
        <Setup {...shared} lastInput={lastInput} onDone={(nextPage) => { update((s) => { s.firstRunDone = true; }); setInSetup(false); setPage(nextPage); }} />
      ) : (
        <div style={{ flex: 1, minHeight: 0, display: 'flex' }}>
          <Sidebar page={page} setPage={setPage} kind={kind} />
          <main style={{ flex: 1, minWidth: 0, overflow: 'auto', padding: '26px 34px 40px' }}>
            {/* One centred column, so a maximized or very wide window keeps a readable layout. */}
            <div style={{ maxWidth: 820, margin: '0 auto' }}>
              {page === 'home' && <Home {...shared} />}
              {page === 'buttons' && <Keymapping {...shared} />}
              {page === 'apps' && <Apps {...shared} registerPicked={(fn) => { appPicked.current = fn; }} />}
              {page === 'settings' && <SettingsPage {...shared} />}
            </div>
          </main>
        </div>
      )}
      <Dialog dialog={dialog} onClose={() => setDialog(null)} />
    </div>
  );
}

import React, { useEffect } from 'react';
import { Button, PageTitle, Select, card, clickable } from './common.jsx';
import { uniqueName, emptyProfile } from './Keymapping.jsx';
import { send } from './bridge.js';

export default function Apps({ settings, update, system, setPage, setEditProfile, registerPicked }) {
  useEffect(() => {
    registerPicked((picked) => update((s) => {
      if (s.apps.some((a) => a.path && picked.path && a.path.toLowerCase() === picked.path.toLowerCase())) return;
      s.apps.push({ id: 'app-' + Date.now(), kind: 'custom', name: picked.name, path: picked.path, rule: s.profileOrder[0] });
    }));
    return () => registerPicked(null);
  }, [registerPicked, update]);

  const rows = settings.apps.map((a) => {
    const resolved = a.kind === 'browser' ? system.defaultBrowser : a.kind === 'player' ? system.defaultPlayer : null;
    const name = a.kind === 'browser' ? 'Default browser' : a.kind === 'player' ? 'Default media player' : a.name;
    const path = resolved ? resolved.name : a.path;
    return { a, name, path, fixed: a.kind === 'xbox' || a.kind === 'store' || a.kind === 'wsettings', removable: a.kind === 'custom' };
  });

  // A new profile for "All other apps" starts as a copy of the one in use, so the controller never goes dead everywhere.
  const setDefaultProfile = (value) => {
    if (value !== 'new') {
      update((s) => { s.activeProfile = value; });
      return;
    }
    const name = uniqueName(settings, 'New profile');
    update((s) => {
      s.profileOrder.push(name);
      s.profiles[name] = structuredClone(s.profiles[s.activeProfile]);
      s.activeProfile = name;
    });
    setEditProfile(name);
    setPage('buttons');
  };

  const setRule = (a, value) => {
    if (value === 'new') {
      const base = a.kind === 'browser' ? (system.defaultBrowser && system.defaultBrowser.name) || 'Browser'
        : a.kind === 'player' ? (system.defaultPlayer && system.defaultPlayer.name) || 'Media' : a.name || 'App';
      const name = uniqueName(settings, base);
      update((s) => {
        s.profileOrder.push(name);
        s.profiles[name] = emptyProfile();
        s.apps.find((x) => x.id === a.id).rule = name;
      });
      setEditProfile(name);
      setPage('buttons');
      return;
    }
    update((s) => { s.apps.find((x) => x.id === a.id).rule = value; });
  };

  return (
    <div>
      <div style={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', gap: 16, flexWrap: 'wrap' }}>
        <PageTitle>App profiles</PageTitle>
        <Button primary onClick={() => send({ type: 'pickApp' })}>Add an app</Button>
      </div>

      <div style={{ ...card, overflow: 'hidden', marginTop: 20 }}>
        {rows.map(({ a, name, path, fixed, removable }) => (
          <div key={a.id} style={{ display: 'flex', alignItems: 'center', gap: 14, padding: '14px 18px', borderBottom: '1px solid rgba(255,255,255,.05)' }}>
            <div style={{ width: 30, height: 30, borderRadius: 6, flex: 'none', background: 'rgba(255,255,255,.07)', display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: 12, fontWeight: 600, color: 'rgba(255,255,255,.7)' }}>{(name || '?').charAt(0)}</div>
            <div style={{ flex: 1, minWidth: 0 }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                <div style={{ fontSize: 14 }}>{name}</div>
                {fixed && <div style={{ flex: 'none', fontSize: 10, padding: '2px 8px', borderRadius: 9, background: 'rgba(255,255,255,.07)', color: 'rgba(255,255,255,.6)' }}>Has its own controller support</div>}
              </div>
              <div style={{ fontSize: 11, color: 'rgba(255,255,255,.45)', marginTop: 2, fontFamily: 'ui-monospace,Consolas,monospace', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{path || 'Not found on this PC'}</div>
              {a.kind === 'steam' && (
                <div style={{ fontSize: 11, color: 'rgba(255,255,255,.55)', marginTop: 4 }}>Big Picture Mode always gets the controller to itself.</div>
              )}
            </div>
            {fixed ? (
              <div style={{ flex: 'none', fontSize: 13, color: 'rgba(255,255,255,.45)', padding: '6px 10px' }}>Off</div>
            ) : (
              <Select value={a.rule} onChange={(v) => setRule(a, v)} style={{ maxWidth: 250 }}>
                {settings.profileOrder.map((p) => <option key={p} value={p}>{p}</option>)}
                <option value="off">Off</option>
                <option value="new">New profile for this app…</option>
              </Select>
            )}
            {removable ? (
              <div {...clickable(() => update((s) => { s.apps = s.apps.filter((x) => x.id !== a.id); }))} className="hov-x" aria-label="Remove"
                style={{ cursor: 'pointer', flex: 'none', fontSize: 14, color: 'rgba(255,255,255,.4)', padding: '0 4px' }}>✕</div>
            ) : (
              <div style={{ flex: 'none', width: 22 }} />
            )}
          </div>
        ))}

        <div style={{ display: 'flex', alignItems: 'center', gap: 14, padding: '14px 18px' }}>
          <div style={{ width: 30, height: 30, borderRadius: 6, flex: 'none', background: 'rgba(255,255,255,.07)', display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: 13, color: 'rgba(255,255,255,.5)' }}>∗</div>
          <div style={{ flex: 1, minWidth: 0 }}>
            <div style={{ fontSize: 14 }}>All other apps</div>
            <div style={{ fontSize: 11, color: 'rgba(255,255,255,.45)', marginTop: 2 }}>Anything without a row of its own uses this profile.</div>
          </div>
          <Select value={settings.activeProfile} onChange={(v) => setDefaultProfile(v)} style={{ maxWidth: 250 }}>
            {settings.profileOrder.map((p) => <option key={p} value={p}>{p}</option>)}
            <option value="new">New profile…</option>
          </Select>
          <div style={{ flex: 'none', width: 22 }} />
        </div>
      </div>

      <div style={{ marginTop: 14, fontSize: 12, color: 'rgba(255,255,255,.5)', lineHeight: 1.5 }}>
        Off means the controller does nothing in that app, and the cursor gets out of the way while you use the controller there. Games get the controller to themselves, in a window or fullscreen. To use the controller as a mouse in a game, add the game here and choose a profile.
      </div>
    </div>
  );
}

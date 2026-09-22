import React from 'react';
import { Button, PageTitle, ToggleRow, card } from './common.jsx';
import { send } from './bridge.js';

const ROWS = [
  { key: 'battery', label: 'Low battery', help: 'Once at 20%, and again at 10%.' },
  { key: 'sound', label: 'Notification sound', help: 'Uses the normal Windows notification sound.' }
];

const sectionLabel = { fontSize: 11, textTransform: 'uppercase', letterSpacing: '.07em', color: 'rgba(255,255,255,.45)', margin: '26px 0 10px' };

export default function SettingsPage({ settings, update, setDialog }) {
  const confirmReset = () => setDialog({
    title: 'Reset profiles to default?',
    body: 'All profiles are replaced by Desktop, Browser and Media with their original button mappings. Profiles you created are removed, and apps that used them switch back to their default profile. Your other settings stay as they are.',
    closeLabel: 'Cancel',
    items: [{
      label: 'Reset profiles', detail: 'This cannot be undone.', initial: '↺',
      onClick: () => { send({ type: 'resetProfiles' }); setDialog(null); }
    }]
  });

  return (
    <div>
      <PageTitle>Settings</PageTitle>

      <div style={sectionLabel}>General</div>
      <div style={{ ...card, overflow: 'hidden' }}>
        <ToggleRow first on={settings.startWithWindows} label="Start with Windows"
          help="Desktop Gamepad starts quietly in the tray when you sign in, and does nothing until a controller connects."
          onToggle={() => update((s) => { s.startWithWindows = !s.startWithWindows; })} />
      </div>

      <div style={sectionLabel}>Notifications</div>
      <div style={{ ...card, overflow: 'hidden' }}>
        {ROWS.map((r, i) => (
          <ToggleRow key={r.key} first={i === 0} on={settings.notify[r.key]} label={r.label} help={r.help}
            onToggle={() => update((s) => { s.notify[r.key] = !s.notify[r.key]; })} />
        ))}
      </div>

      <div style={sectionLabel}>Profiles</div>
      <div style={{ ...card, display: 'flex', alignItems: 'center', gap: 16, padding: '16px 18px' }}>
        <div style={{ flex: 1, minWidth: 0 }}>
          <div style={{ fontSize: 14 }}>Reset profiles to default</div>
          <div style={{ fontSize: 12, lineHeight: 1.55, color: 'rgba(255,255,255,.545)', marginTop: 4 }}>Brings back the Desktop, Browser and Media profiles with their original button mappings.</div>
        </div>
        <Button danger onClick={confirmReset}>Reset</Button>
      </div>
    </div>
  );
}

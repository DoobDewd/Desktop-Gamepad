import React, { useState } from 'react';
import { INPUTS, INPUT_GROUPS, LAYER_ACTION, artFor, glyphFor, labelFor } from './data.js';
import { Button, Glyph, PageTitle, Select, card, clickable } from './common.jsx';
import Picker from './Picker.jsx';

export function uniqueName(settings, base) {
  let name = base, n = 2;
  while (settings.profileOrder.includes(name)) name = base + ' ' + n++;
  return name;
}

export function emptyProfile() {
  const m = {};
  INPUTS.forEach((i) => { m[i.id] = { base: {}, layer: {} }; });
  return m;
}

export default function Keymapping({ settings, update, kind, setDialog, editProfile, setEditProfile, heldInputs }) {
  const [renaming, setRenaming] = useState(false);
  const [renameValue, setRenameValue] = useState('');
  const [layerHeld, setLayerHeld] = useState(false);
  const [layerLocked, setLayerLocked] = useState(false);
  const [picker, setPicker] = useState(null);

  const profile = editProfile && settings.profiles[editProfile] ? editProfile : settings.activeProfile;
  const map = settings.profiles[profile] || {};
  const holderInput = INPUTS.find((i) => map[i.id] && map[i.id].base && map[i.id].base.tap === LAYER_ACTION);
  const holder = holderInput ? glyphFor(holderInput, kind) : null;
  // The second layer shows while the chip is held or locked, or while the layer button is held on the controller.
  const layer = layerHeld || layerLocked || !!(holderInput && heldInputs && heldInputs.includes(holderInput.id));

  const addProfile = (base, copyFrom) => {
    const name = uniqueName(settings, base);
    update((s) => {
      s.profileOrder.push(name);
      s.profiles[name] = copyFrom ? structuredClone(s.profiles[copyFrom]) : emptyProfile();
    });
    setEditProfile(name);
  };

  const saveRename = () => {
    const next = renameValue.trim();
    setRenaming(false);
    if (!next || next === profile) return;
    const name = uniqueName(settings, next);
    update((s) => {
      s.profiles[name] = s.profiles[profile];
      delete s.profiles[profile];
      s.profileOrder = s.profileOrder.map((p) => (p === profile ? name : p));
      s.apps.forEach((a) => { if (a.rule === profile) a.rule = name; });
      if (s.activeProfile === profile) s.activeProfile = name;
    });
    setEditProfile(name);
  };

  const deleteProfile = () => {
    if (settings.profileOrder.length < 2) return;
    const used = settings.apps.filter((a) => a.rule === profile).map((a) => appLabel(a));
    const fallback = settings.profileOrder.filter((p) => p !== profile)[0];
    setDialog({
      title: 'Delete “' + profile + '”?',
      body: used.length
        ? 'Its mappings are lost. ' + used.join(', ') + (used.length === 1 ? ' goes' : ' go') + ' back to ' + fallback + '.'
        : 'Its mappings are lost. No app is using it.',
      closeLabel: 'Cancel',
      items: [{
        label: 'Delete this profile', detail: 'This cannot be undone.', initial: '✕',
        onClick: () => {
          update((s) => {
            delete s.profiles[profile];
            s.profileOrder = s.profileOrder.filter((p) => p !== profile);
            s.apps.forEach((a) => { if (a.rule === profile) a.rule = fallback; });
            if (s.activeProfile === profile) s.activeProfile = fallback;
          });
          setEditProfile(fallback);
          setDialog(null);
        }
      }]
    });
  };

  const clear = (id) => update((s) => { s.profiles[profile][id][layer ? 'layer' : 'base'] = {}; });

  const chip = {
    bg: layer ? 'rgba(224,178,60,.16)' : 'rgba(255,255,255,.05)',
    border: layer ? 'rgba(224,178,60,.5)' : 'rgba(255,255,255,.1)',
    fg: layer ? '#f0cd7a' : 'rgba(255,255,255,.85)',
    dot: layer ? '#e0b23c' : 'rgba(255,255,255,.3)',
    hint: layer ? (layerLocked ? 'showing second layer · click to release' : 'showing second layer') : 'press and hold to preview, or click to toggle',
    label: holder ? 'Hold ' + holder : 'No layer button set'
  };

  return (
    <div>
      <div style={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', gap: 20, flexWrap: 'wrap', marginBottom: 18 }}>
        <div>
          <PageTitle>Keymapping</PageTitle>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginTop: 14, flexWrap: 'wrap' }}>
            {renaming ? (
              <>
                <input autoFocus value={renameValue} onChange={(e) => setRenameValue(e.target.value)}
                  onKeyDown={(e) => { if (e.key === 'Enter') saveRename(); if (e.key === 'Escape') setRenaming(false); }}
                  style={{ font: 'inherit', fontSize: 13, color: '#fff', background: 'rgba(255,255,255,.05)', border: '1px solid rgba(255,255,255,.1)', borderBottom: '1px solid rgba(96,205,255,.7)', borderRadius: 5, padding: '6px 10px', width: 170, outline: 'none' }} />
                <Button primary onClick={saveRename} style={{ fontSize: 12, padding: '6px 14px' }}>Save</Button>
                <Button onClick={() => setRenaming(false)} style={{ fontSize: 12, padding: '6px 12px' }}>Cancel</Button>
              </>
            ) : (
              <>
                <div style={{ fontSize: 12, color: 'rgba(255,255,255,.5)' }}>Profile</div>
                <Select value={profile} onChange={(v) => {
                  if (v === '__new') addProfile('New profile', null);
                  else setEditProfile(v);
                }}>
                  {settings.profileOrder.map((p) => <option key={p} value={p}>{p}</option>)}
                  <option value="__new">New profile…</option>
                </Select>
                <Button onClick={() => { setRenameValue(profile); setRenaming(true); }} style={{ fontSize: 12, padding: '6px 12px' }}>Rename</Button>
                <Button onClick={() => addProfile(profile + ' copy', profile)} style={{ fontSize: 12, padding: '6px 12px' }}>Duplicate</Button>
                <Button danger disabled={settings.profileOrder.length < 2} onClick={deleteProfile} style={{ fontSize: 12, padding: '6px 12px' }}>Delete</Button>
              </>
            )}
          </div>
        </div>
        <div {...clickable(() => { setLayerLocked(!layerLocked); setLayerHeld(false); })}
          onMouseDown={() => setLayerHeld(true)} onMouseUp={() => setLayerHeld(false)} onMouseLeave={() => setLayerHeld(false)}
          style={{ cursor: 'pointer', userSelect: 'none', display: 'flex', alignItems: 'center', gap: 10, padding: '8px 14px', borderRadius: 20, background: chip.bg, border: '1px solid ' + chip.border, color: chip.fg }}>
          <div style={{ width: 7, height: 7, borderRadius: '50%', background: chip.dot }} />
          <div style={{ fontSize: 13, fontWeight: 600 }}>{chip.label}</div>
          <div style={{ fontSize: 12, opacity: .75 }}>{chip.hint}</div>
        </div>
      </div>

      {profile === 'Browser' && (
        <div style={{ fontSize: 12, lineHeight: 1.55, color: 'rgba(255,255,255,.55)', margin: '-4px 0 14px' }}>
          The Browser layout follows the keys video sites use, YouTube above all: View full screens the video, and Y opens
          search. Change anything you like; your changes are kept.
        </div>
      )}

      <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
        {INPUT_GROUPS.map((grp) => (
          <div key={grp.title} style={{ ...card, overflow: 'hidden' }}>
            <div style={{ fontSize: 11, textTransform: 'uppercase', letterSpacing: '.07em', color: 'rgba(255,255,255,.45)', padding: '12px 18px 8px' }}>{grp.title}</div>
            {grp.ids.map((id) => {
              const input = INPUTS.find((i) => i.id === id);
              const m = map[id] || { base: {}, layer: {} };
              const action = (layer ? m.layer : m.base).tap || '';
              return (
                <div key={id} style={{ display: 'flex', alignItems: 'center', gap: 12, padding: '9px 18px', borderTop: '1px solid rgba(255,255,255,.05)' }}>
                  <div style={{ width: 32, flex: 'none', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                    <Glyph img={artFor(id, kind)} text={glyphFor(input, kind)} />
                  </div>
                  <div style={{ flex: 1, minWidth: 0, fontSize: 13, color: 'rgba(255,255,255,.9)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{labelFor(input, kind)}</div>
                  <div {...clickable(() => setPicker({ input: id, layer, subtitle: labelFor(input, kind) + (layer ? ' · while holding ' + (holder || 'the layer button') : '') }))} className="hov-field"
                    style={{ cursor: 'pointer', flex: 'none', width: 186, display: 'flex', alignItems: 'center', gap: 8, height: 32, padding: '0 10px', borderRadius: 5, background: '#383838', border: '1px solid rgba(255,255,255,.1)' }}>
                    <div style={{ flex: 1, minWidth: 0, fontSize: 13, color: action ? (layer ? '#f0cd7a' : '#fff') : 'rgba(255,255,255,.3)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{action || 'Not set'}</div>
                    <div style={{ flex: 'none', fontSize: 8, color: 'rgba(255,255,255,.55)' }}>▼</div>
                  </div>
                  <div {...clickable(() => clear(id))} className="hov-clear" aria-label="Clear"
                    style={{ cursor: 'pointer', flex: 'none', width: 22, height: 32, display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: 11, color: action ? 'rgba(255,255,255,.3)' : 'rgba(255,255,255,.1)' }}>✕</div>
                </div>
              );
            })}
          </div>
        ))}
      </div>

      {picker && (
        <Picker
          subtitle={picker.subtitle}
          current={((picker.layer ? map[picker.input].layer : map[picker.input].base).tap) || ''}
          onClose={() => setPicker(null)}
          onPick={(action) => {
            update((s) => {
              const pm = s.profiles[profile];
              if (action === LAYER_ACTION) {
                // Only one input may hold the layer; assigning it here clears it everywhere else.
                Object.values(pm).forEach((mm) => ['base', 'layer'].forEach((set) => {
                  Object.keys(mm[set]).forEach((press) => { if (mm[set][press] === LAYER_ACTION) delete mm[set][press]; });
                }));
              }
              pm[picker.input][picker.layer ? 'layer' : 'base'].tap = action;
            });
            setPicker(null);
          }}
        />
      )}
    </div>
  );
}

export function appLabel(a, system) {
  if (a.kind === 'browser') return 'Default browser';
  if (a.kind === 'player') return 'Default media player';
  return a.name || a.path || 'App';
}

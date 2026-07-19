const byId = (id) => document.getElementById(id);
const workspace = byId('workspace');
const inspection = byId('inspection');
const result = byId('result');
const checked = (id) => byId(id).checked && !byId(id).disabled;
const number = (id) => Number(byId(id).value);
let inspectedGame = null;

async function post(url, body = {}) {
  const response = await fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
  const data = await response.json();
  if (!response.ok) throw new Error(data.error || data.detail || 'Operación fallida');
  return data;
}

function setStatus(element, message, state = 'neutral') { element.textContent = message; element.className = `status ${state}`; }
function setDependentState(key, enabled) {
  document.querySelectorAll(`[data-requires="${key}"]`).forEach((element) => {
    element.classList.toggle('is-disabled', !enabled);
    element.querySelectorAll('input').forEach((control) => { control.disabled = !enabled; });
  });
}
function activeCount(selector) { return [...document.querySelectorAll(selector)].filter((control) => control.checked && !control.disabled).length; }
function getEvolutionMode() {
  const selected = document.querySelector('[data-evolution] input:checked');
  return selected ? selected.closest('[data-evolution]').dataset.evolution : 'None';
}
function updateUi() {
  setDependentState('stats', checked('stats'));
  setDependentState('types', checked('types'));
  setDependentState('egg-groups', checked('egg-groups'));
  setDependentState('learnsets', checked('learnsets'));
  setDependentState('egg-moves', checked('egg-moves'));
  byId('base-exp-percent').disabled = !checked('base-exp');
  byId('fixed-catch-rate-value').disabled = !checked('fixed-catch-rate');
  const evolutionMode = getEvolutionMode();
  setDependentState('evolution-filters', evolutionMode === 'Replacements' || evolutionMode === 'EveryLevel');
  const personalCount = activeCount('[data-group="personal"] input[type="checkbox"]') - activeCount('[data-stat-choice] input') - (checked('wonder-guard') ? 1 : 0);
  const hasPersonalChanges = personalCount > 0;
  const hasLearnsets = checked('learnsets');
  const hasEggMoves = checked('egg-moves');
  const moveCount = activeCount('[data-group="moves"] input[type="checkbox"]');
  const hasMoves = moveCount > 0;
  const hasEvolutions = evolutionMode !== 'None';
  const groupCount = Number(hasPersonalChanges) + Number(hasLearnsets) + Number(hasEggMoves) + Number(hasMoves) + Number(hasEvolutions);
  byId('personal-state').textContent = hasPersonalChanges ? `${personalCount} ${personalCount === 1 ? 'opción' : 'opciones'}` : 'Sin cambios';
  byId('moves-state').textContent = hasMoves ? `${moveCount} ${moveCount === 1 ? 'opción' : 'opciones'}` : 'Sin cambios';
  byId('evolution-state').textContent = hasEvolutions ? 'Activo' : 'Sin cambios';
  byId('step-options').textContent = groupCount ? `${groupCount} ${groupCount === 1 ? 'grupo activo' : 'grupos activos'}` : 'Sin cambios';
  byId('selection-summary').textContent = groupCount ? `${groupCount} ${groupCount === 1 ? 'grupo con cambios' : 'grupos con cambios'}` : 'Sin cambios seleccionados';
  const summary = [];
  if (hasPersonalChanges) summary.push('datos de Pokémon');
  if (hasLearnsets) summary.push('movimientos por nivel');
  if (hasEggMoves) summary.push('movimientos huevo');
  if (hasMoves) summary.push('datos de movimientos');
  if (hasEvolutions) summary.push('evoluciones');
  byId('selection-detail').textContent = summary.length ? `${summary.join(', ')}.` : 'Marca al menos una opción para continuar.';
  byId('randomize').disabled = !inspectedGame?.titleId || groupCount === 0;
}
function resetOptions() {
  document.querySelectorAll('#changes input[type="checkbox"]').forEach((control) => { control.checked = false; });
  ['abilities', 'wonder-guard', 'stat-hp', 'stat-atk', 'stat-def', 'stat-spa', 'stat-spd', 'stat-spe', 'learnsets', 'expand', 'spread', 'stab', 'power-order', 'egg-expand', 'egg-stab', 'evo-bst'].forEach((id) => { byId(id).checked = true; });
  Object.assign(byId('stat-deviation'), { value: 25 }); Object.assign(byId('same-type'), { value: 50 }); Object.assign(byId('same-egg'), { value: 50 });
  Object.assign(byId('move-count'), { value: 25 }); Object.assign(byId('max-level'), { value: 75 }); Object.assign(byId('stab-percent'), { value: 52.3 });
  Object.assign(byId('egg-move-count'), { value: 18 }); Object.assign(byId('egg-stab-percent'), { value: 32.1 });
  Object.assign(byId('base-exp-percent'), { value: 100 }); Object.assign(byId('fixed-catch-rate-value'), { value: 45 });
  updateUi();
}
async function inspectWorkspace() {
  const button = byId('inspect'); button.disabled = true; setStatus(inspection, 'Cargando el juego…');
  try {
    const data = await post('/api/workspace/inspect', { workspacePath: workspace.value });
    inspectedGame = data;
    const titleNote = data.titleId ? 'Title ID detectado automáticamente.' : 'Falta exheader.bin: selecciona la carpeta completa del juego para poder exportar.';
    setStatus(inspection, `Listo: ${data.gameVersion}. ${titleNote}`, data.titleId ? 'success' : 'error');
    byId('step-game').textContent = data.gameVersion;
    setStatus(result, data.titleId ? 'Todo preparado. Pulsa Exportar cuando quieras.' : 'No se puede crear el LayeredFS sin detectar el Title ID.', data.titleId ? 'neutral' : 'error');
  } catch (error) {
    inspectedGame = null; byId('step-game').textContent = 'Revisa la carpeta'; setStatus(inspection, error.message, 'error'); setStatus(result, 'Carga un juego válido para continuar.', 'neutral');
  } finally { button.disabled = false; updateUi(); }
}
byId('browse').addEventListener('click', async () => {
  const button = byId('browse'); button.disabled = true;
  try { const data = await post('/api/workspace/pick'); workspace.value = data.path; await inspectWorkspace(); }
  catch (error) { setStatus(inspection, error.message, 'neutral'); }
  finally { button.disabled = false; }
});
byId('inspect').addEventListener('click', inspectWorkspace);
workspace.addEventListener('input', () => {
  if (!inspectedGame) return;
  inspectedGame = null; byId('step-game').textContent = 'Ruta modificada'; setStatus(inspection, 'La ruta cambió. Cárgala otra vez antes de exportar.', 'neutral'); setStatus(result, 'Carga de nuevo el juego para continuar.', 'neutral'); updateUi();
});
document.querySelectorAll('#changes input').forEach((control) => control.addEventListener('change', updateUi));
document.querySelectorAll('[data-evolution] input').forEach((control) => control.addEventListener('change', () => {
  if (control.checked) document.querySelectorAll('[data-evolution] input').forEach((other) => { if (other !== control) other.checked = false; });
  updateUi();
}));
byId('reset-options').addEventListener('click', resetOptions);
byId('randomize').addEventListener('click', async () => {
  const button = byId('randomize'); button.disabled = true; setStatus(result, 'Elige una carpeta para guardar el LayeredFS…');
  try {
    const output = await post('/api/workspace/pick-output');
    setStatus(result, 'Generando archivos y empaquetando LayeredFS…');
    const data = await post('/api/jobs/randomize', {
      workspacePath: workspace.value, outputDirectory: output.path, titleId: inspectedGame.titleId, language: 1,
      randomizeAbilities: checked('abilities'), randomizeHeldItems: checked('held-items'), randomizeLearnsets: checked('learnsets'),
      personal: { randomizeAbilities: checked('abilities'), allowWonderGuard: checked('wonder-guard'), randomizeHeldItems: checked('held-items'), randomizeCatchRate: checked('catch-rate'), randomizeTmCompatibility: checked('tm'), randomizeHmCompatibility: checked('hm'), randomizeTypeTutors: checked('type-tutors'), randomizeMoveTutors: checked('move-tutors'), randomizeStats: checked('stats'), shuffleStats: checked('shuffle-stats'), statsToRandomize: ['stat-hp', 'stat-atk', 'stat-def', 'stat-spa', 'stat-spd', 'stat-spe'].map(checked), statDeviation: number('stat-deviation'), randomizeTypes: checked('types'), sameTypeChance: number('same-type'), randomizeEggGroups: checked('egg-groups'), sameEggGroupChance: number('same-egg'), removeEvYields: checked('no-evs'), setFastGrowth: checked('fast-growth'), baseExperiencePercent: checked('base-exp') ? number('base-exp-percent') : null, quickHatch: checked('quick-hatch'), setCatchRate: checked('fixed-catch-rate') ? number('fixed-catch-rate-value') : null, removeTutorCompatibility: checked('remove-tutors'), fullTmCompatibility: checked('full-tm'), fullHmCompatibility: checked('full-hm'), fullMoveTutorCompatibility: checked('full-tutor') },
      learnsets: { enabled: checked('learnsets'), expand: checked('expand'), moveCount: number('move-count'), spread: checked('spread'), maxLevel: number('max-level'), stab: checked('stab'), stabPercent: number('stab-percent'), orderByPower: checked('power-order'), fourMovesAtLevel1: checked('four-level-one'), excludeFixedDamage: checked('no-fixed') },
      eggMoves: { enabled: checked('egg-moves'), expand: checked('egg-expand'), moveCount: number('egg-move-count'), stab: checked('egg-stab'), stabPercent: number('egg-stab-percent') },
      moves: { randomizeType: checked('move-types'), randomizeCategory: checked('move-categories'), metronomeMode: checked('metronome-mode') },
      evolutions: { mode: getEvolutionMode(), matchBst: checked('evo-bst'), matchExperience: checked('evo-exp'), matchType: checked('evo-type'), includeLegendary: checked('evo-legendary'), includeMythical: checked('evo-mythical') },
    });
    setStatus(result, `Listo. ZIP: ${data.zipPath} · Archivos modificados: ${data.changedFiles.join(', ')}`, 'success');
  } catch (error) { setStatus(result, error.message, 'error'); }
  finally { updateUi(); }
});
updateUi();

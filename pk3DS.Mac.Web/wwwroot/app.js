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
function moduleAvailable(id) {
  const module = inspectedGame?.modules?.find((entry) => entry.id === id);
  return module ? module.sourceAvailable : true;
}
function setDependentState(key, enabled) {
  document.querySelectorAll(`[data-requires="${key}"]`).forEach((element) => {
    element.classList.toggle('is-disabled', !enabled);
    element.querySelectorAll('input').forEach((control) => {
      control.disabled = !enabled || control.dataset.gameSpecificDisabled === 'true';
    });
  });
}
function activeCount(selector) { return [...document.querySelectorAll(selector)].filter((control) => control.checked && !control.disabled).length; }
function getEvolutionMode() {
  const selected = document.querySelector('[data-evolution] input:checked');
  return selected ? selected.closest('[data-evolution]').dataset.evolution : 'None';
}
function setGameSpecificState(id, enabled, message) {
  const input = byId(id);
  const card = input?.closest('.check-card, .toggle, .option-group') || input;
  if (!input || !card) return;
  input.dataset.gameSpecificDisabled = String(!enabled);
  input.disabled = !enabled;
  if (!enabled) input.checked = false;
  card.classList.toggle('is-disabled', !enabled);
  card.setAttribute('aria-disabled', String(!enabled));
  card.title = enabled ? '' : message;
}
function setTrainerMoveOptionState(id, enabled) {
  const input = byId(id);
  const card = input?.closest('.check-card, .field');
  if (!input || !card) return;
  const disabled = !checked('trainer-randomizer') || !enabled;
  input.disabled = disabled;
  card.classList.toggle('is-disabled', disabled);
  card.setAttribute('aria-disabled', String(disabled));
}
function updateTrainerMoveOptions() {
  const randomMoves = checked('trainer-moves');
  const currentMoves = checked('trainer-current-moves');
  setTrainerMoveOptionState('trainer-exclude-fixed-damage', randomMoves || currentMoves);
  setTrainerMoveOptionState('trainer-ensure-damaging', randomMoves);
  setTrainerMoveOptionState('trainer-ensure-stab', randomMoves);
  setTrainerMoveOptionState('trainer-damaging-count', randomMoves && checked('trainer-ensure-damaging'));
  setTrainerMoveOptionState('trainer-stab-count', randomMoves && checked('trainer-ensure-stab'));
}
function updateGameSpecificOptions() {
  const version = inspectedGame?.gameVersion;
  const generation6 = ['XY', 'ORAS', 'ORASDEMO'].includes(version);
  const generation7 = ['SM', 'USUM'].includes(version);
  setGameSpecificState('abilities', moduleAvailable('personal'), 'Este workspace no contiene el GARC personal.');
  setGameSpecificState('held-items', moduleAvailable('personal'), 'Este workspace no contiene el GARC personal.');
  setGameSpecificState('catch-rate', moduleAvailable('personal'), 'Este workspace no contiene el GARC personal.');
  setGameSpecificState('types', moduleAvailable('personal'), 'Este workspace no contiene el GARC personal.');
  setGameSpecificState('egg-groups', moduleAvailable('personal'), 'Este workspace no contiene el GARC personal.');
  setGameSpecificState('stats', moduleAvailable('personal'), 'Este workspace no contiene el GARC personal.');
  setGameSpecificState('tm', moduleAvailable('personal'), 'Este workspace no contiene el GARC personal.');
  setGameSpecificState('type-tutors', moduleAvailable('personal'), 'Este workspace no contiene el GARC personal.');
  setGameSpecificState('move-tutors', (version === 'ORAS' || version === 'USUM') && moduleAvailable('personal'), 'OR/AS y US/UM contienen estos tutores y requieren el GARC personal.');
  setGameSpecificState('wonder-guard', moduleAvailable('personal'), 'Este workspace no contiene el GARC personal.');
  setGameSpecificState('learnsets', moduleAvailable('levelup'), 'Este workspace no contiene el GARC levelup.');
  setGameSpecificState('learn-metronome', (generation6 || generation7) && moduleAvailable('levelup'), 'Los movimientos por nivel requieren un juego de Gen. VI o Gen. VII y el GARC levelup.');
  setGameSpecificState('learn-allow-hms', generation6 && moduleAvailable('levelup'), 'Permitir HMs solo está definido para el randomizador de movimientos por nivel de Gen. VI.');
  setGameSpecificState('egg-moves', moduleAvailable('eggmove'), 'Este workspace no contiene el GARC eggmove.');
  setGameSpecificState('egg-allow-hms', generation6 && moduleAvailable('eggmove'), 'Permitir HMs solo está definido para el randomizador de movimientos huevo de Gen. VI.');
  setGameSpecificState('move-types', moduleAvailable('moves'), 'Este workspace no contiene el GARC move.');
  setGameSpecificState('move-categories', moduleAvailable('moves'), 'Este workspace no contiene el GARC move.');
  setGameSpecificState('metronome-mode', moduleAvailable('moves'), 'Este workspace no contiene el GARC move.');
  setGameSpecificState('evo-randomize', moduleAvailable('evolutions'), 'Este workspace no contiene el GARC evolution.');
  setGameSpecificState('evo-no-trades', moduleAvailable('evolutions'), 'Este workspace no contiene el GARC evolution.');
  setGameSpecificState('evo-every-level', moduleAvailable('evolutions'), 'Este workspace no contiene el GARC evolution.');
  setGameSpecificState('hm', generation6 && moduleAvailable('personal'), 'Sol/Luna y Ultrasol/Ultraluna no tienen MO o falta el GARC personal.');
  setGameSpecificState('full-hm', generation6 && moduleAvailable('personal'), 'Sol/Luna y Ultrasol/Ultraluna no tienen MO o falta el GARC personal.');
  for (const id of ['no-evs', 'fast-growth', 'quick-hatch', 'remove-tutors', 'full-tm', 'full-tutor', 'base-exp', 'fixed-catch-rate'])
    setGameSpecificState(id, moduleAvailable('personal'), 'Este workspace no contiene el GARC personal.');
  setGameSpecificState('full-beach-tutor', version === 'USUM' && moduleAvailable('personal'), 'Los tutores de playa solo existen en Ultrasol y Ultraluna.');
  setGameSpecificState('wild', (generation6 || generation7) && moduleAvailable('wild'), 'Los encuentros salvajes requieren sus GARCs completos de Gen. VI o Gen. VII.');
  setGameSpecificState('wild-hordes', generation6, 'Las hordas son exclusivas de los encuentros Gen. VI.');
  setGameSpecificState('trainer-randomizer', (generation6 || generation7) && moduleAvailable('trainers'), 'Los equipos requieren trclass, trdata, trpoke y gametext del juego.');
  setGameSpecificState('trainer-prizes', generation6 && moduleAvailable('trainers'), 'Los premios aleatorios de entrenadores solo están disponibles en Gen. VI.');
  setGameSpecificState('trainer-fill-gen6', !inspectedGame || (generation6 && moduleAvailable('trainers')), 'Los equipos importantes de seis Pokémon solo están definidos para Gen. VI.');
  setGameSpecificState('trainer-important-teams', generation7 && moduleAvailable('trainers'), 'Los equipos importantes de seis Pokémon solo están definidos para Gen. VII.');
  setGameSpecificState('trainer-high-power', moduleAvailable('levelup'), 'Los ataques potentes requieren el GARC levelup.');
  setGameSpecificState('trainer-current-moves', moduleAvailable('levelup'), 'Los movimientos por nivel requieren el GARC levelup.');
  setGameSpecificState('trainer-natures', generation7 && moduleAvailable('trainers'), 'Las naturalezas aleatorias solo están disponibles en Gen. VII.');
  setGameSpecificState('trainer-shiny', generation7 && moduleAvailable('trainers'), 'El shiny de los equipos solo está disponible en Gen. VII.');
  setGameSpecificState('trainer-shedinja', generation7 && moduleAvailable('trainers'), 'Gen. VI incluye Shedinja automáticamente; esta selección solo aplica a Gen. VII.');
  setGameSpecificState('trainer-only-singles', generation6 && moduleAvailable('trainers'), 'El filtro de combates individuales solo está disponible para las clases de Gen. VI.');
  setGameSpecificState('trainer-type-themes', (generation6 || generation7) && moduleAvailable('trainers') && moduleAvailable('personal'), 'Los temas por tipo requieren los equipos y el GARC personal.');
  setGameSpecificState('trainer-gym-themes', generation6 && moduleAvailable('trainers') && moduleAvailable('personal'), 'Los temas de entrenadores de gimnasio solo están disponibles en Gen. VI y requieren el GARC personal.');
  setGameSpecificState('trainer-gym-elite-only', generation6 && moduleAvailable('trainers') && moduleAvailable('personal'), 'El filtro de gimnasios y Alto Mando solo está disponible en Gen. VI.');
  setGameSpecificState('trainer-story-megas', !inspectedGame || (generation6 && moduleAvailable('trainers') && moduleAvailable('personal')), 'Las Megaevoluciones de historia solo están disponibles en equipos de Gen. VI.');
  setGameSpecificState('trainer-mega-forms', (generation6 || generation7) && moduleAvailable('trainers') && moduleAvailable('personal'), 'Las formas Mega aleatorias requieren un equipo Gen. VI/VII y el GARC personal.');
  setGameSpecificState('trainer-gen-7', !inspectedGame || (generation7 && moduleAvailable('trainers')), 'Gen. VII no está disponible en los juegos Gen. VI.');
}
function updateUi() {
  updateGameSpecificOptions();
  setDependentState('stats', checked('stats'));
  setDependentState('types', checked('types'));
  setDependentState('egg-groups', checked('egg-groups'));
  setDependentState('learnsets', checked('learnsets'));
  setDependentState('egg-moves', checked('egg-moves'));
  setDependentState('wild', checked('wild'));
  setDependentState('wild-species', checked('wild-species'));
  setDependentState('wild-levels', checked('wild-levels'));
  setDependentState('trainer-randomizer', checked('trainer-randomizer'));
  setDependentState('trainer-classes', checked('trainer-classes'));
  setDependentState('trainer-levels', checked('trainer-levels'));
  setDependentState('trainer-force-evolved', checked('trainer-force-evolved'));
  setDependentState('trainer-prizes', checked('trainer-prizes'));
  setDependentState('trainer-composition', checked('trainer-composition'));
  setDependentState('trainer-high-power', checked('trainer-high-power'));
  setDependentState('trainer-shiny', checked('trainer-shiny'));
  setDependentState('trainer-species', checked('trainer-species'));
  setDependentState('trainer-type-themes', checked('trainer-type-themes'));
  updateTrainerMoveOptions();
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
  const hasWild = checked('wild') && (checked('wild-species') || checked('wild-levels'));
  const hasTrainerRandomizer = checked('trainer-randomizer') && (checked('trainer-species') || checked('trainer-match-bst') || checked('trainer-legendary') || checked('trainer-mythical') || checked('trainer-shedinja') || checked('trainer-levels') || checked('trainer-classes') || checked('trainer-composition') || checked('trainer-items') || checked('trainer-abilities') || checked('trainer-moves') || checked('trainer-current-moves') || checked('trainer-metronome') || checked('trainer-ai') || checked('trainer-max-ivs') || checked('trainer-force-evolved') || checked('trainer-prizes') || checked('trainer-fill-gen6') || checked('trainer-important-teams') || checked('trainer-high-power') || checked('trainer-natures') || checked('trainer-shiny') || checked('trainer-type-themes') || checked('trainer-mega-forms') || checked('trainer-story-megas') || checked('trainer-gym-elite-only') || checked('trainer-gym-themes'));
  const groupCount = Number(hasPersonalChanges) + Number(hasLearnsets) + Number(hasEggMoves) + Number(hasMoves) + Number(hasEvolutions) + Number(hasWild) + Number(hasTrainerRandomizer);
  byId('personal-state').textContent = hasPersonalChanges ? `${personalCount} ${personalCount === 1 ? 'opción' : 'opciones'}` : 'Sin cambios';
  byId('moves-state').textContent = hasMoves ? `${moveCount} ${moveCount === 1 ? 'opción' : 'opciones'}` : 'Sin cambios';
  byId('evolution-state').textContent = hasEvolutions ? 'Activo' : 'Sin cambios';
  byId('wild-state').textContent = hasWild ? 'Activo' : 'Sin cambios';
  byId('trainer-randomizer-state').textContent = hasTrainerRandomizer ? 'Activo' : 'Sin cambios';
  byId('step-options').textContent = groupCount ? `${groupCount} ${groupCount === 1 ? 'grupo activo' : 'grupos activos'}` : 'Sin cambios';
  byId('selection-summary').textContent = groupCount ? `${groupCount} ${groupCount === 1 ? 'grupo con cambios' : 'grupos con cambios'}` : 'Sin cambios seleccionados';
  const summary = [];
  if (hasPersonalChanges) summary.push('datos de Pokémon');
  if (hasLearnsets) summary.push('movimientos por nivel');
  if (hasEggMoves) summary.push('movimientos huevo');
  if (hasMoves) summary.push('datos de movimientos');
  if (hasEvolutions) summary.push('evoluciones');
  if (hasWild) summary.push('encuentros salvajes');
  if (hasTrainerRandomizer) summary.push('equipos de entrenadores');
  byId('selection-detail').textContent = summary.length ? `${summary.join(', ')}.` : 'Marca al menos una opción para continuar.';
  byId('randomize').disabled = !inspectedGame?.titleId || groupCount === 0;
}
function resetOptions() {
  document.querySelectorAll('#changes input[type="checkbox"]').forEach((control) => { control.checked = false; });
  ['abilities', 'wonder-guard', 'stat-hp', 'stat-atk', 'stat-def', 'stat-spa', 'stat-spd', 'stat-spe', 'learnsets', 'expand', 'spread', 'stab', 'power-order', 'egg-expand', 'egg-stab', 'evo-bst', 'wild-species', 'wild-bst', 'trainer-species', 'trainer-ignore-special', 'trainer-legendary', 'trainer-mythical', 'trainer-story-megas', 'trainer-gen-1', 'trainer-gen-2', 'trainer-gen-3', 'trainer-gen-4', 'trainer-gen-5', 'trainer-gen-6', 'trainer-gen-7', 'trainer-exclude-fixed-damage', 'trainer-ensure-damaging', 'trainer-ensure-stab'].forEach((id) => { byId(id).checked = true; });
  Object.assign(byId('stat-deviation'), { value: 25 }); Object.assign(byId('same-type'), { value: 50 }); Object.assign(byId('same-egg'), { value: 50 });
  Object.assign(byId('move-count'), { value: 25 }); Object.assign(byId('max-level'), { value: 75 }); Object.assign(byId('stab-percent'), { value: 52.3 });
  Object.assign(byId('egg-move-count'), { value: 18 }); Object.assign(byId('egg-stab-percent'), { value: 32.1 });
  Object.assign(byId('wild-level-multiplier'), { value: 1.00 });
  Object.assign(byId('trainer-level-multiplier'), { value: 1.00 });
  Object.assign(byId('trainer-force-evolved-level'), { value: 50 });
  Object.assign(byId('trainer-prize-chance'), { value: 15 });
  Object.assign(byId('trainer-min-team-size'), { value: 1 }); Object.assign(byId('trainer-max-team-size'), { value: 6 });
  Object.assign(byId('trainer-high-power-level'), { value: 50 });
  Object.assign(byId('trainer-damaging-count'), { value: 2 }); Object.assign(byId('trainer-stab-count'), { value: 2 });
  Object.assign(byId('trainer-shiny-chance'), { value: 3 });
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
document.querySelectorAll('[data-trainer-move-mode] input').forEach((control) => control.addEventListener('change', () => {
  if (control.checked)
    document.querySelectorAll('[data-trainer-move-mode] input').forEach((other) => { if (other !== control) other.checked = false; });
  updateUi();
}));
document.querySelectorAll('[data-evolution] input').forEach((control) => control.addEventListener('change', () => {
  if (control.checked) document.querySelectorAll('[data-evolution] input').forEach((other) => { if (other !== control) other.checked = false; });
  updateUi();
}));
byId('reset-options').addEventListener('click', resetOptions);
byId('randomize').addEventListener('click', async () => {
  const button = byId('randomize'); button.disabled = true; setStatus(result, 'Elige una carpeta para guardar el LayeredFS…');
  try {
    const seedText = byId('randomizer-seed').value.trim();
    const seed = seedText ? Number(seedText) : null;
    if (seedText && (!Number.isInteger(seed) || seed < -2147483648 || seed > 2147483647))
      throw new Error('La semilla debe ser un entero de 32 bits.');
    const output = await post('/api/workspace/pick-output');
    setStatus(result, 'Generando archivos y empaquetando LayeredFS…');
    const data = await post('/api/jobs/randomize', {
      workspacePath: workspace.value, outputDirectory: output.path, titleId: inspectedGame.titleId, language: 2,
      seed,
      randomizeAbilities: checked('abilities'), randomizeHeldItems: checked('held-items'), randomizeLearnsets: checked('learnsets'),
      personal: { randomizeAbilities: checked('abilities'), allowWonderGuard: checked('wonder-guard'), randomizeHeldItems: checked('held-items'), randomizeCatchRate: checked('catch-rate'), randomizeTmCompatibility: checked('tm'), randomizeHmCompatibility: checked('hm'), randomizeTypeTutors: checked('type-tutors'), randomizeMoveTutors: checked('move-tutors'), randomizeStats: checked('stats'), shuffleStats: checked('shuffle-stats'), statsToRandomize: ['stat-hp', 'stat-atk', 'stat-def', 'stat-spa', 'stat-spd', 'stat-spe'].map(checked), statDeviation: number('stat-deviation'), randomizeTypes: checked('types'), sameTypeChance: number('same-type'), randomizeEggGroups: checked('egg-groups'), sameEggGroupChance: number('same-egg'), removeEvYields: checked('no-evs'), setFastGrowth: checked('fast-growth'), baseExperiencePercent: checked('base-exp') ? number('base-exp-percent') : null, quickHatch: checked('quick-hatch'), setCatchRate: checked('fixed-catch-rate') ? number('fixed-catch-rate-value') : null, removeTutorCompatibility: checked('remove-tutors'), fullTmCompatibility: checked('full-tm'), fullHmCompatibility: checked('full-hm'), fullMoveTutorCompatibility: checked('full-tutor'), fullBeachTutorCompatibility: checked('full-beach-tutor') },
      learnsets: { enabled: checked('learnsets'), expand: checked('expand'), moveCount: number('move-count'), spread: checked('spread'), maxLevel: number('max-level'), stab: checked('stab'), stabPercent: number('stab-percent'), orderByPower: checked('power-order'), fourMovesAtLevel1: checked('four-level-one'), excludeFixedDamage: checked('no-fixed'), allowHMs: checked('learn-allow-hms'), metronomeMode: checked('learn-metronome') },
      eggMoves: { enabled: checked('egg-moves'), expand: checked('egg-expand'), moveCount: number('egg-move-count'), stab: checked('egg-stab'), stabPercent: number('egg-stab-percent'), allowHMs: checked('egg-allow-hms') },
      moves: { randomizeType: checked('move-types'), randomizeCategory: checked('move-categories'), metronomeMode: checked('metronome-mode') },
      evolutions: { mode: getEvolutionMode(), matchBst: checked('evo-bst'), matchExperience: checked('evo-exp'), matchType: checked('evo-type'), includeLegendary: checked('evo-legendary'), includeMythical: checked('evo-mythical') },
      wild: { enabled: checked('wild'), randomizeSpecies: checked('wild-species'), randomizeLevels: checked('wild-levels'), levelMultiplier: number('wild-level-multiplier'), homogeneousHordes: checked('wild-hordes'), matchBst: checked('wild-bst'), includeLegendary: checked('wild-legendary'), includeMythical: checked('wild-mythical') },
      trainers: { enabled: checked('trainer-randomizer'), randomizeSpecies: checked('trainer-species'), randomizeLevels: checked('trainer-levels'), levelMultiplier: number('trainer-level-multiplier'), randomizeClasses: checked('trainer-classes'), randomizeComposition: checked('trainer-composition'), minTeamSize: number('trainer-min-team-size'), maxTeamSize: number('trainer-max-team-size'), ignoreSpecialClasses: checked('trainer-ignore-special'), onlySinglesForClasses: checked('trainer-only-singles'), randomizeItems: checked('trainer-items'), randomizeAbilities: checked('trainer-abilities'), randomizeMoves: checked('trainer-moves'), useCurrentLearnsetMoves: checked('trainer-current-moves'), metronomeMoves: checked('trainer-metronome'), excludeFixedDamageMoves: checked('trainer-exclude-fixed-damage'), ensureDamagingMoves: checked('trainer-ensure-damaging'), damagingMoveCount: number('trainer-damaging-count'), ensureStabMoves: checked('trainer-ensure-stab'), stabMoveCount: number('trainer-stab-count'), maximizeAI: checked('trainer-ai'), maximizeIVs: checked('trainer-max-ivs'), forceFullyEvolved: checked('trainer-force-evolved'), fullyEvolvedLevel: number('trainer-force-evolved-level'), randomizePrizes: checked('trainer-prizes'), fillImportantGen6Teams: checked('trainer-fill-gen6'), fillImportantGen7Teams: checked('trainer-important-teams'), forceHighPower: checked('trainer-high-power'), highPowerLevel: number('trainer-high-power-level'), randomizeNature: checked('trainer-natures'), randomizeShiny: checked('trainer-shiny'), shinyChance: number('trainer-shiny-chance'), randomizeTypeThemes: checked('trainer-type-themes'), allowMegaForms: checked('trainer-mega-forms'), includeGymTrainerThemes: checked('trainer-gym-themes'), gymEliteOnlyThemes: checked('trainer-gym-elite-only'), ensureStoryMegaEvolutions: checked('trainer-story-megas'), matchBst: checked('trainer-match-bst'), includeLegendary: checked('trainer-legendary'), includeMythical: checked('trainer-mythical'), includeShedinja: checked('trainer-shedinja'), allowGen1: byId('trainer-gen-1').checked, allowGen2: byId('trainer-gen-2').checked, allowGen3: byId('trainer-gen-3').checked, allowGen4: byId('trainer-gen-4').checked, allowGen5: byId('trainer-gen-5').checked, allowGen6: byId('trainer-gen-6').checked, allowGen7: byId('trainer-gen-7').checked },
    });
    setStatus(result, `Listo. ZIP: ${data.zipPath} · Archivos modificados: ${data.changedFiles.join(', ')}${seed === null ? '' : ` · Semilla: ${seed}`}`, 'success');
  } catch (error) { setStatus(result, error.message, 'error'); }
  finally { updateUi(); }
});
updateUi();

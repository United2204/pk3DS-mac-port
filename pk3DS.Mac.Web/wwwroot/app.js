const byId = (id) => document.getElementById(id);
const workspace = byId('workspace');
const inspection = byId('inspection');
const result = byId('result');
const checked = (id) => byId(id).checked;
const number = (id) => Number(byId(id).value);

async function post(url, body) {
  const response = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
  const data = await response.json();
  if (!response.ok) throw new Error(data.error || data.detail || 'Operación fallida');
  return data;
}

byId('inspect').addEventListener('click', async () => {
  inspection.textContent = 'Comprobando…';
  try {
    const data = await post('/api/workspace/inspect', { workspacePath: workspace.value });
    inspection.textContent = `Detectado: ${data.gameVersion}. ${data.note}`;
    inspection.className = 'status success';
  } catch (error) {
    inspection.textContent = error.message;
    inspection.className = 'status error';
  }
});

byId('randomize').addEventListener('click', async () => {
  const button = byId('randomize');
  button.disabled = true;
  result.textContent = 'Generando archivos y empaquetando LayeredFS…';
  result.className = 'status';
  try {
    const data = await post('/api/jobs/randomize', {
      workspacePath: workspace.value,
      outputDirectory: byId('output').value || null,
      titleId: byId('title-id').value.trim(),
      language: Number(byId('language').value),
      randomizeAbilities: byId('abilities').checked,
      randomizeHeldItems: byId('held-items').checked,
      randomizeLearnsets: byId('learnsets').checked,
      personal: {
        randomizeAbilities: checked('abilities'), allowWonderGuard: checked('wonder-guard'),
        randomizeHeldItems: checked('held-items'), randomizeCatchRate: checked('catch-rate'),
        randomizeTmCompatibility: checked('tm'), randomizeHmCompatibility: checked('hm'),
        randomizeTypeTutors: checked('type-tutors'), randomizeMoveTutors: checked('move-tutors'),
        randomizeStats: checked('stats'), shuffleStats: checked('shuffle-stats'),
        statDeviation: number('stat-deviation'), randomizeTypes: checked('types'),
        sameTypeChance: number('same-type'), randomizeEggGroups: checked('egg-groups'),
        sameEggGroupChance: number('same-egg'),
      },
      learnsets: {
        enabled: checked('learnsets'), expand: checked('expand'), moveCount: number('move-count'),
        spread: checked('spread'), maxLevel: number('max-level'), stab: checked('stab'),
        stabPercent: number('stab-percent'), stabFirst: checked('stab-first'),
        orderByPower: checked('power-order'), fourMovesAtLevel1: checked('four-level-one'),
        excludeFixedDamage: checked('no-fixed'),
      },
      evolutions: {
        mode: byId('evolution-mode').value, matchBst: checked('evo-bst'), matchExperience: checked('evo-exp'),
        matchType: checked('evo-type'), includeLegendary: checked('evo-legendary'), includeMythical: checked('evo-mythical'),
      },
    });
    result.textContent = `Listo. ZIP: ${data.zipPath} · Archivos modificados: ${data.changedFiles.join(', ')}`;
    result.className = 'status success';
  } catch (error) {
    result.textContent = error.message;
    result.className = 'status error';
  } finally {
    button.disabled = false;
  }
});

import { describe, expect, it } from 'vitest';
import { buildSchoolsTree } from '../features/schools/schools-tree';

function node(path: string, courseCount: number) {
  const parts = path.split(' > ');
  return { path, name: parts.at(-1)!, level: parts.length - 1, courseCount };
}

describe('schools tree', () => {
  it('hides a redundant root while preserving the original lookup path', () => {
    const tree = buildSchoolsTree([
      node('Portal', 3),
      node('Portal > Escola A', 2),
      node('Portal > Escola B', 1),
    ]);

    expect([...tree.children.keys()]).toEqual(['Escola A', 'Escola B']);
    expect(tree.children.get('Escola A')?.path).toBe('Portal > Escola A');
  });

  it('keeps an independent root when another root has child categories', () => {
    const tree = buildSchoolsTree([
      node('Portal > Escola A', 2),
      node('Outro portal', 1),
    ]);

    expect([...tree.children.keys()]).toEqual(['Escola A', 'Outro portal']);
  });
});

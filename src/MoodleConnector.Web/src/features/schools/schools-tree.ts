import type { Course, CourseHierarchyNode } from '../courses/courses-gateway';

export type TreeNode = { name: string; path: string; count: number; children: Map<string, TreeNode> };

function splitPath(path: string) {
  return path.split('>').map((part) => part.trim()).filter(Boolean);
}

export function normalizeCategoryPath(path: string) {
  return splitPath(path).join(' > ').toLocaleLowerCase('pt-BR');
}

export function courseCategoryPath(course: Pick<Course, 'categoryName'>) {
  const parts = splitPath(course.categoryName ?? '');
  return (parts.length > 0 ? parts : ['Sem categoria']).join(' > ');
}

export function groupCoursesByCategory<T extends Pick<Course, 'categoryName'>>(courses: T[]) {
  const groups = new Map<string, T[]>();
  courses.forEach((course) => {
    const key = normalizeCategoryPath(courseCategoryPath(course));
    const group = groups.get(key) ?? [];
    group.push(course);
    groups.set(key, group);
  });
  return groups;
}

export function countCoursesByCategory<T extends Pick<Course, 'categoryName'>>(courses: T[]) {
  const counts = new Map<string, number>();
  courses.forEach((course) => {
    const parts = splitPath(courseCategoryPath(course));
    for (let length = 1; length <= parts.length; length += 1) {
      const key = normalizeCategoryPath(parts.slice(0, length).join(' > '));
      counts.set(key, (counts.get(key) ?? 0) + 1);
    }
  });
  return counts;
}

function getRedundantRoots(items: CourseHierarchyNode[]) {
  return new Set(
    items
      .map((item) => splitPath(item.path))
      .filter((parts) => parts.length > 1)
      .map((parts) => parts[0].toLocaleLowerCase('pt-BR')),
  );
}

export function buildSchoolsTree(items: CourseHierarchyNode[]) {
  const root: TreeNode = { name: 'root', path: '', count: 0, children: new Map() };
  const redundantRoots = getRedundantRoots(items);

  for (const item of items) {
    const parts = splitPath(item.path);
    const shouldHideRedundantRoot = parts[0] !== undefined && redundantRoots.has(parts[0].toLocaleLowerCase('pt-BR'));
    const offset = shouldHideRedundantRoot ? 1 : 0;
    if (parts.length <= offset) continue;

    let node = root;
    for (let index = offset; index < parts.length; index += 1) {
      const part = parts[index];
      const path = parts.slice(0, index + 1).join(' > ');
      if (!node.children.has(part)) node.children.set(part, { name: part, path, count: 0, children: new Map() });
      node = node.children.get(part)!;
    }
    node.count = item.courseCount;
  }
  return root;
}

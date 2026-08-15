import { Calendar, Clock3 } from 'lucide-react';
import { Link } from 'react-router-dom';

import { Badge } from '@/components/ui/badge';
import { Card, CardContent } from '@/components/ui/card';
import type { Course } from '../courses-gateway';
import { getCourseLifecycle } from '../course-status';

function formatDate(value?: string) {
  if (!value) return '—';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? '—' : date.toLocaleDateString('pt-BR');
}

function lifecycle(course: Course) {
  const status = getCourseLifecycle(course);
  if (status === 'finished') return { label: 'Finalizada', className: 'border-slate-300 bg-slate-100 text-slate-700' };
  if (status === 'not_started') return { label: 'Não iniciada', className: 'border-amber-200 bg-amber-50 text-amber-700' };
  return { label: 'Em andamento', className: 'border-emerald-200 bg-emerald-50 text-emerald-700' };
}

export function CourseCard({ course }: { course: Course }) {
  const status = lifecycle(course);
  const title = course.displayName ?? course.fullName;
  return <Link to={`/cursos/${encodeURIComponent(course.connectionRef)}/${encodeURIComponent(course.courseId)}`} className="block h-full"><Card className="card-interactive h-full"><CardContent className="p-5"><div className="space-y-4"><div className="flex items-start justify-between gap-2"><div className="min-w-0 space-y-2"><h2 className="line-clamp-2 font-semibold leading-tight">{title}</h2><p className="truncate text-xs text-muted-foreground">{course.shortName ?? course.categoryName ?? 'Curso Moodle'}</p><Badge variant="outline" className={`text-[10px] ${status.className}`}>{status.label}</Badge></div></div><div className="grid grid-cols-2 gap-3 border-y py-3"><div className="text-center"><p className="text-lg font-semibold">{course.progress != null ? `${Math.round(course.progress)}%` : '—'}</p><p className="text-[11px] text-muted-foreground">Progresso</p></div><div className="text-center"><p className="text-sm font-semibold">{formatDate(course.endDate)}</p><p className="text-[11px] text-muted-foreground">Fim</p></div></div><div className="space-y-1 text-xs text-muted-foreground"><div className="flex items-center gap-2"><Calendar className="h-3 w-3" />Início: {formatDate(course.startDate)}</div><div className="flex items-center gap-2"><Clock3 className="h-3 w-3" />Último acesso: {formatDate(course.lastAccessAt)}</div></div></div></CardContent></Card></Link>;
}

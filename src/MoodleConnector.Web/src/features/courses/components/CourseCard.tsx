import { Calendar, Clock3, ExternalLink, ImageOff, Users } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router-dom';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';
import type { Course } from '../courses-gateway';

function formatDate(value?: string) {
  if (!value) return '—';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? '—' : date.toLocaleDateString('pt-BR');
}

function lifecycle(course: Course) {
  const now = Date.now();
  const start = course.startDate ? new Date(course.startDate).getTime() : undefined;
  const end = course.endDate ? new Date(course.endDate).getTime() : undefined;
  if (end && end < now) return { label: 'Finalizada', className: 'border-slate-300 bg-slate-100 text-slate-700' };
  if (start && start > now) return { label: 'Não iniciada', className: 'border-amber-200 bg-amber-50 text-amber-700' };
  return { label: 'Em andamento', className: 'border-emerald-200 bg-emerald-50 text-emerald-700' };
}

export function CourseCard({ course }: { course: Course }) {
  const [imageFailed, setImageFailed] = useState(false);
  const status = lifecycle(course);
  const title = course.displayName ?? course.fullName;

  return (
    <Card className="group h-full overflow-hidden transition-all hover:-translate-y-0.5 hover:border-primary/30 hover:shadow-md">
      {course.courseImage && !imageFailed ? (
        <div className="relative h-28 overflow-hidden bg-muted">
          <img src={course.courseImage} alt="" className="h-full w-full object-cover transition-transform group-hover:scale-105" onError={() => setImageFailed(true)} />
        </div>
      ) : (
        <div className="flex h-20 items-center justify-center bg-gradient-to-br from-primary/10 via-muted to-card text-primary/35">
          <ImageOff className="h-6 w-6" aria-hidden="true" />
        </div>
      )}
      <CardContent className="space-y-4 p-5">
        <div className="space-y-2">
          <div className="flex items-start justify-between gap-3">
            <div className="min-w-0">
              <h2 className="line-clamp-2 font-semibold leading-tight">{title}</h2>
              <p className="mt-1 truncate text-xs text-muted-foreground">{course.shortName ?? course.categoryName ?? 'Curso Moodle'}</p>
            </div>
            <Badge variant="outline" className={`shrink-0 text-[10px] ${status.className}`}>{status.label}</Badge>
          </div>
        </div>

        <div className="grid grid-cols-2 gap-3 border-y py-3">
          <div className="text-center">
            <Users className="mx-auto h-3.5 w-3.5 text-muted-foreground" />
            <p className="mt-1 text-lg font-semibold">{course.progress != null ? `${Math.round(course.progress)}%` : '—'}</p>
            <p className="text-[11px] text-muted-foreground">Progresso</p>
          </div>
          <div className="text-center">
            <Calendar className="mx-auto h-3.5 w-3.5 text-muted-foreground" />
            <p className="mt-1 text-sm font-semibold">{formatDate(course.endDate)}</p>
            <p className="text-[11px] text-muted-foreground">Fim</p>
          </div>
        </div>

        <div className="space-y-1 text-xs text-muted-foreground">
          <div className="flex items-center gap-2"><Calendar className="h-3 w-3" /> Início: {formatDate(course.startDate)}</div>
          <div className="flex items-center gap-2"><Clock3 className="h-3 w-3" /> Último acesso: {formatDate(course.lastAccessAt)}</div>
        </div>

        <Button asChild className="w-full gap-2">
          <Link to={`/cursos/${encodeURIComponent(course.connectionRef)}/${encodeURIComponent(course.courseId)}`}>
            Abrir curso <ExternalLink className="h-4 w-4" />
          </Link>
        </Button>
      </CardContent>
    </Card>
  );
}

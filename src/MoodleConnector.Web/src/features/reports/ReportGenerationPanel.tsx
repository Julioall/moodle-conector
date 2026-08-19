import { CheckCircle2, FileSpreadsheet, Loader2, X } from 'lucide-react';
import { useMutation } from '@tanstack/react-query';

import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';
import type { Course } from '../courses/courses-gateway';
import { reportsGateway } from './reports-gateway';

export function ReportGenerationPanel({
  connectionRef,
  courses,
  onClear,
  onCompleted,
}: {
  connectionRef: string;
  courses: Course[];
  onClear: () => void;
  onCompleted?: () => void;
}) {
  const mutation = useMutation({
    mutationFn: () => reportsGateway.createJob({
      reportType: 'grades',
      scopeType: 'courses',
      connectionRef,
      courseIds: courses.map((course) => course.courseId),
    }),
    onSuccess: () => (onCompleted ?? onClear)(),
  });

  if (courses.length === 0) return null;

  const turmaCount = new Set(courses.map((course) => course.categoryName ?? 'Sem turma')).size;

  return (
    <Card className="border-primary/30 bg-primary/[0.03]">
      <CardContent className="flex flex-col gap-3 p-4 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex min-w-0 items-start gap-3">
          <div className="mt-0.5 rounded-lg bg-primary/10 p-2 text-primary"><FileSpreadsheet className="h-4 w-4" /></div>
          <div className="min-w-0">
            <p className="font-medium">Relatório de notas</p>
            <p className="text-sm text-muted-foreground">
              {courses.length} {courses.length === 1 ? 'curso selecionado' : 'cursos selecionados'} em {turmaCount} {turmaCount === 1 ? 'turma' : 'turmas'}. Será gerado um Excel por turma.
            </p>
            {mutation.isSuccess && <p className="mt-1 flex items-center gap-1 text-sm text-emerald-700"><CheckCircle2 className="h-4 w-4" />Solicitado. O download ficará disponível na notificação.</p>}
            {mutation.isError && <p className="mt-1 text-sm text-destructive">Não foi possível solicitar o relatório. Tente novamente.</p>}
          </div>
        </div>
        <div className="flex shrink-0 items-center gap-2">
          <Button type="button" variant="ghost" size="sm" onClick={onClear} disabled={mutation.isPending}><X className="mr-1.5 h-4 w-4" />Limpar</Button>
          <Button type="button" size="sm" onClick={() => mutation.mutate()} disabled={mutation.isPending || mutation.isSuccess}>
            {mutation.isPending && <Loader2 className="mr-1.5 h-4 w-4 animate-spin" />}
            Gerar Excel
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}

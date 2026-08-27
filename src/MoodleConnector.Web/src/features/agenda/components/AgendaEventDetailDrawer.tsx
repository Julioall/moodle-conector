import { CalendarDays, Clock3, Edit2, Trash2 } from 'lucide-react';

import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from '@/components/ui/sheet';
import { PlannerReferenceTags } from '@/features/tasks/PlannerReferenceTags';
import type { AgendaEvent } from '../agenda-gateway';

const eventTypeLabels: Record<string, string> = {
  manual: 'Compromisso',
  meeting: 'Reunião',
  class: 'Aula',
  deadline: 'Prazo',
};

function formatDateTime(value?: string) {
  if (!value) return 'Não informado';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? 'Não informado' : date.toLocaleString('pt-BR', { dateStyle: 'medium', timeStyle: 'short' });
}

type AgendaEventDetailDrawerProps = {
  event: AgendaEvent | null;
  onClose: () => void;
  onEdit: (event: AgendaEvent) => void;
  onDelete: (id: string) => void;
};

export function AgendaEventDetailDrawer({ event, onClose, onEdit, onDelete }: AgendaEventDetailDrawerProps) {
  return (
    <Sheet open={Boolean(event)} onOpenChange={(open) => { if (!open) onClose(); }}>
      <SheetContent side="right" className="flex w-full flex-col gap-0 p-0 sm:max-w-lg">
        <SheetHeader className="border-b px-6 pb-4 pt-6 text-left">
          <div className="flex items-start gap-3 pr-7">
            <div className="mt-0.5 flex h-9 w-9 shrink-0 items-center justify-center rounded-full border bg-primary/10 text-primary"><CalendarDays className="h-4 w-4" /></div>
            <div className="min-w-0"><SheetTitle>{event?.title}</SheetTitle><SheetDescription>{event?.description ? 'Detalhes deste compromisso.' : 'Compromisso da agenda.'}</SheetDescription></div>
          </div>
        </SheetHeader>
        {event && <>
          <div className="grid gap-4 border-b px-6 py-4 sm:grid-cols-2">
            <div><p className="text-xs font-medium text-muted-foreground">Tipo</p><Badge variant="outline" className="mt-1">{eventTypeLabels[event.type] ?? event.type}</Badge></div>
            <div><p className="text-xs font-medium text-muted-foreground">Início</p><p className="mt-1 flex items-center gap-1 text-xs text-muted-foreground"><CalendarDays className="h-3.5 w-3.5" />{formatDateTime(event.startAt)}</p></div>
            <div className="sm:col-span-2"><p className="text-xs font-medium text-muted-foreground">Fim</p><p className="mt-1 flex items-center gap-1 text-xs text-muted-foreground"><Clock3 className="h-3.5 w-3.5" />{formatDateTime(event.endAt)}</p></div>
          </div>
          <div className="flex-1 space-y-4 overflow-auto px-6 py-5">
            <div><h3 className="text-sm font-semibold">Descrição</h3><p className="mt-2 whitespace-pre-wrap text-sm text-muted-foreground">{event.description || 'Este evento não possui descrição.'}</p></div>
            {event.references?.length ? <div><h3 className="text-sm font-semibold">Vínculos</h3><div className="mt-2"><PlannerReferenceTags references={event.references} /></div></div> : null}
            <div className="rounded-lg bg-muted/50 p-3 text-xs text-muted-foreground"><p>Criado em {formatDateTime(event.createdAt)}</p><p className="mt-1">Atualizado em {formatDateTime(event.updatedAt)}</p></div>
          </div>
          <div className="flex gap-2 border-t px-6 py-4"><Button type="button" className="flex-1" onClick={() => onEdit(event)}><Edit2 className="mr-2 h-4 w-4" />Editar evento</Button><Button type="button" variant="outline" onClick={() => onDelete(event.id)}><Trash2 className="mr-2 h-4 w-4" />Remover</Button></div>
        </>}
      </SheetContent>
    </Sheet>
  );
}

import { Calendar, Clock3, Edit2, Trash2 } from 'lucide-react';

import { Button } from '@/components/ui/button';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from '@/components/ui/sheet';
import { cn } from '@/lib/utils';
import type { Task, TaskPriority, TaskStatus } from '../tasks-gateway';

const statusLabels: Record<TaskStatus, string> = {
  todo: 'A fazer',
  in_progress: 'Em andamento',
  done: 'Concluída',
};

const priorityLabels: Record<TaskPriority, string> = {
  low: 'Baixa',
  medium: 'Média',
  high: 'Alta',
  urgent: 'Urgente',
};

const priorityStyles: Record<TaskPriority, string> = {
  low: 'bg-slate-100 text-slate-700 dark:bg-slate-800 dark:text-slate-300',
  medium: 'bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-300',
  high: 'bg-amber-100 text-amber-700 dark:bg-amber-900/40 dark:text-amber-300',
  urgent: 'bg-red-100 text-red-700 dark:bg-red-900/40 dark:text-red-300',
};

const statuses: TaskStatus[] = ['todo', 'in_progress', 'done'];

function formatDueDate(value?: string) {
  if (!value) return 'Sem prazo';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? 'Sem prazo' : date.toLocaleDateString('pt-BR');
}

function isOverdue(task: Task) {
  if (!task.dueAt || task.status === 'done') return false;
  const due = new Date(task.dueAt);
  if (Number.isNaN(due.getTime())) return false;
  const today = new Date();
  return due < new Date(today.getFullYear(), today.getMonth(), today.getDate() + 1);
}

type TaskDetailDrawerProps = {
  task: Task | null;
  onClose: () => void;
  onEdit?: (task: Task) => void;
  onDelete?: (id: string) => void;
  onStatusChange?: (id: string, status: TaskStatus) => void;
};

export function TaskDetailDrawer({ task, onClose, onEdit, onDelete, onStatusChange }: TaskDetailDrawerProps) {
  return (
    <Sheet open={Boolean(task)} onOpenChange={(open) => { if (!open) onClose(); }}>
      <SheetContent side="right" className="flex w-full flex-col gap-0 p-0 sm:max-w-lg">
        <SheetHeader className="border-b px-6 pb-4 pt-6 text-left">
          <SheetTitle>{task?.title}</SheetTitle>
          {task?.description ? <SheetDescription>{task.description}</SheetDescription> : <SheetDescription>Detalhes da tarefa operacional.</SheetDescription>}
        </SheetHeader>
        {task && <>
          <div className="grid gap-4 border-b px-6 py-4 sm:grid-cols-2">
            <div><p className="text-xs font-medium text-muted-foreground">Prioridade</p><span className={cn('mt-1 inline-flex rounded-full px-2 py-0.5 text-xs font-medium', priorityStyles[task.priority])}>{priorityLabels[task.priority]}</span></div>
            <div><p className="text-xs font-medium text-muted-foreground">Prazo</p><p className={cn('mt-1 flex items-center gap-1 text-sm', isOverdue(task) && 'text-destructive')}><Calendar className="h-3.5 w-3.5" />{formatDueDate(task.dueAt)}</p></div>
            {onStatusChange && <label className="grid gap-1.5 text-xs font-medium sm:col-span-2">Status<Select value={task.status} onValueChange={(value) => onStatusChange(task.id, value as TaskStatus)}><SelectTrigger className="h-9 text-sm"><SelectValue /></SelectTrigger><SelectContent>{statuses.map((status) => <SelectItem key={status} value={status}>{statusLabels[status]}</SelectItem>)}</SelectContent></Select></label>}
          </div>
          <div className="flex-1 space-y-4 overflow-auto px-6 py-5">
            <div><h3 className="text-sm font-semibold">Descrição</h3><p className="mt-2 whitespace-pre-wrap text-sm text-muted-foreground">{task.description || 'Esta tarefa não possui descrição.'}</p></div>
            <div className="rounded-lg bg-muted/50 p-3 text-xs text-muted-foreground"><p className="flex items-center gap-1.5"><Clock3 className="h-3.5 w-3.5" />Status: {statusLabels[task.status]}</p><p className="mt-1">Criada em {new Date(task.createdAt).toLocaleString('pt-BR')}</p><p className="mt-1">Atualizada em {new Date(task.updatedAt).toLocaleString('pt-BR')}</p></div>
          </div>
          {(onEdit || onDelete) && <div className="flex gap-2 border-t px-6 py-4">{onEdit && <Button type="button" className="flex-1" onClick={() => onEdit(task)}><Edit2 className="mr-2 h-4 w-4" />Editar tarefa</Button>}{onDelete && <Button type="button" variant="outline" onClick={() => onDelete(task.id)}><Trash2 className="mr-2 h-4 w-4" />Remover</Button>}</div>}
        </>}
      </SheetContent>
    </Sheet>
  );
}

import { useEffect, useMemo, useState, type DragEvent, type FormEvent, type MouseEvent } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Calendar,
  CalendarRange,
  CheckSquare,
  ChevronLeft,
  ChevronRight,
  Edit2,
  LayoutDashboard,
  List,
  ListFilter,
  Plus,
  Trash2,
} from 'lucide-react';

import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { Input } from '@/components/ui/input';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { Textarea } from '@/components/ui/textarea';
import { cn } from '@/lib/utils';
import { TaskDetailDrawer } from './components/TaskDetailDrawer';
import { tasksGateway, type Task, type TaskInput, type TaskPriority, type TaskStatus } from './tasks-gateway';

type ViewMode = 'list' | 'kanban';
type StatusFilter = 'all' | TaskStatus;
type DateWindow = 'all' | 'today' | 'week' | 'overdue';

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

const columns: { status: TaskStatus; label: string; header: string; border: string }[] = [
  { status: 'todo', label: 'A fazer', header: 'bg-slate-100 text-slate-700 dark:bg-slate-800 dark:text-slate-300', border: 'border-slate-300 dark:border-slate-600' },
  { status: 'in_progress', label: 'Em andamento', header: 'bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-300', border: 'border-blue-300 dark:border-blue-700' },
  { status: 'done', label: 'Concluída', header: 'bg-green-100 text-green-700 dark:bg-green-900/40 dark:text-green-300', border: 'border-green-300 dark:border-green-700' },
];

function dateInputValue(value?: string) {
  if (!value) return '';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '';
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
}

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
  const startOfTomorrow = new Date(today.getFullYear(), today.getMonth(), today.getDate() + 1);
  return due < startOfTomorrow;
}

function matchesDateWindow(value: string | undefined, window: DateWindow) {
  if (window === 'all') return true;
  if (!value) return false;
  const due = new Date(value);
  if (Number.isNaN(due.getTime())) return false;
  const today = new Date();
  const start = new Date(today.getFullYear(), today.getMonth(), today.getDate());
  if (window === 'overdue') return due < start;
  if (window === 'today') return due >= start && due < new Date(start.getTime() + 86_400_000);
  return due >= start && due < new Date(start.getTime() + 7 * 86_400_000);
}

function TaskCard({
  task,
  onEdit,
  onDelete,
  onStatusChange,
  onOpen,
}: {
  task: Task;
  onEdit: (task: Task) => void;
  onDelete: (id: string) => void;
  onStatusChange: (id: string, status: TaskStatus) => void;
  onOpen: (task: Task) => void;
}) {
  const stopPropagation = (event: MouseEvent) => event.stopPropagation();

  return (
    <div className={cn('group cursor-pointer rounded-lg border bg-card p-4 shadow-sm transition-all hover:shadow-md', task.status === 'done' && 'opacity-70')} onClick={() => onOpen(task)}>
      <div className="flex items-start gap-3">
          <input
            type="checkbox"
            checked={task.status === 'done'}
            onChange={(event) => onStatusChange(task.id, event.target.checked ? 'done' : 'todo')}
            onClick={stopPropagation}
            className="mt-0.5 h-4 w-4 cursor-pointer rounded accent-primary"
            aria-label={task.status === 'done' ? 'Marcar como pendente' : 'Marcar como concluída'}
          />
          <div className="min-w-0 flex-1">
            <p className={cn('line-clamp-2 text-sm font-medium leading-snug', task.status === 'done' && 'text-muted-foreground line-through')}>{task.title}</p>
            {task.description && <p className="mt-1 line-clamp-2 text-xs text-muted-foreground">{task.description}</p>}
            <div className="mt-2 flex flex-wrap items-center gap-2">
              <span className={cn('shrink-0 rounded-full px-2 py-0.5 text-[11px] font-medium', priorityStyles[task.priority])}>{priorityLabels[task.priority]}</span>
              <span className={cn('inline-flex items-center gap-1', isOverdue(task) ? 'text-destructive' : 'text-muted-foreground')}>
                <Calendar className="h-3.5 w-3.5" />
                {formatDueDate(task.dueAt)}
                {isOverdue(task) && ' · Atrasada'}
              </span>
            </div>
          </div>
          <div className="flex shrink-0 items-center gap-1 opacity-0 transition-opacity group-hover:opacity-100" onClick={stopPropagation}>
            <Button type="button" size="icon" variant="ghost" className="h-7 w-7" onClick={() => onEdit(task)} aria-label={`Editar ${task.title}`}>
              <Edit2 className="h-3.5 w-3.5" />
            </Button>
            <Button type="button" size="icon" variant="ghost" className="h-7 w-7 hover:text-destructive" onClick={() => onDelete(task.id)} aria-label={`Remover ${task.title}`}>
              <Trash2 className="h-3.5 w-3.5" />
            </Button>
          </div>
      </div>
    </div>
  );
}

export function TasksPage() {
  const client = useQueryClient();
  const [viewMode, setViewMode] = useState<ViewMode>('kanban');
  const [statusFilter, setStatusFilter] = useState<StatusFilter>('all');
  const [priorityFilter, setPriorityFilter] = useState<'all' | TaskPriority>('all');
  const [dateWindow, setDateWindow] = useState<DateWindow>('today');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [formOpen, setFormOpen] = useState(false);
  const [detailTask, setDetailTask] = useState<Task | null>(null);
  const [draggingTaskId, setDraggingTaskId] = useState<string | null>(null);
  const [dropTargetStatus, setDropTargetStatus] = useState<TaskStatus | null>(null);
  const [editingTask, setEditingTask] = useState<Task | null>(null);
  const [deleteId, setDeleteId] = useState<string | null>(null);
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [priority, setPriority] = useState<TaskPriority>('medium');
  const [dueAt, setDueAt] = useState('');
  const [defaultStatus, setDefaultStatus] = useState<TaskStatus>('todo');

  const query = useQuery({
    queryKey: ['app', 'tasks', priorityFilter],
    queryFn: () => tasksGateway.list(1, 100, undefined, priorityFilter === 'all' ? undefined : priorityFilter),
    staleTime: 30_000,
  });
  const tasks = useMemo(() => query.data?.data ?? [], [query.data?.data]);
  const baseFilteredTasks = useMemo(() => {
    const normalizedSearch = search.trim().toLocaleLowerCase('pt-BR');
    return tasks.filter((task) => (!normalizedSearch || task.title.toLocaleLowerCase('pt-BR').includes(normalizedSearch)) && matchesDateWindow(task.dueAt, dateWindow));
  }, [dateWindow, search, tasks]);
  const filteredTasks = useMemo(
    () => viewMode === 'list' && statusFilter !== 'all' ? baseFilteredTasks.filter((task) => task.status === statusFilter) : baseFilteredTasks,
    [baseFilteredTasks, statusFilter, viewMode],
  );
  const tasksByStatus = useMemo(() => Object.fromEntries(columns.map((column) => [column.status, filteredTasks.filter((task) => task.status === column.status)])) as Record<TaskStatus, Task[]>, [filteredTasks]);
  const pageSize = 24;
  const total = filteredTasks.length;
  const totalPages = Math.max(1, Math.ceil(total / pageSize));
  const paginatedTasks = useMemo(() => filteredTasks.slice((page - 1) * pageSize, page * pageSize), [filteredTasks, page]);
  const paginatedTasksByStatus = useMemo(() => Object.fromEntries(columns.map((column) => [column.status, paginatedTasks.filter((task) => task.status === column.status)])) as Record<TaskStatus, Task[]>, [paginatedTasks]);

  const create = useMutation({
    mutationFn: (input: TaskInput) => tasksGateway.create(input),
    onSuccess: () => { setFormOpen(false); resetForm(); void client.invalidateQueries({ queryKey: ['app', 'tasks'] }); },
  });
  const update = useMutation({
    mutationFn: ({ id, input }: { id: string; input: TaskInput }) => tasksGateway.update(id, input),
    onSuccess: () => { setFormOpen(false); resetForm(); void client.invalidateQueries({ queryKey: ['app', 'tasks'] }); },
  });
  const remove = useMutation({
    mutationFn: tasksGateway.remove,
    onSuccess: () => { setDeleteId(null); setDetailTask(null); void client.invalidateQueries({ queryKey: ['app', 'tasks'] }); },
  });

  function resetForm() {
    setEditingTask(null);
    setTitle('');
    setDescription('');
    setPriority('medium');
    setDueAt('');
    setDefaultStatus('todo');
  }

  function openCreate(status: TaskStatus = 'todo') {
    resetForm();
    setDefaultStatus(status);
    setFormOpen(true);
  }

  function openEdit(task: Task) {
    setDetailTask(null);
    setEditingTask(task);
    setTitle(task.title);
    setDescription(task.description ?? '');
    setPriority(task.priority);
    setDueAt(dateInputValue(task.dueAt));
    setDefaultStatus(task.status);
    setFormOpen(true);
  }

  function changeFilter<T>(setter: (value: T) => void, value: T) {
    setter(value);
    setPage(1);
  }

  function handleDragStart(event: DragEvent<HTMLDivElement>, task: Task) {
    setDraggingTaskId(task.id);
    event.dataTransfer.effectAllowed = 'move';
    event.dataTransfer.setData('text/plain', task.id);
  }

  function handleDrop(event: DragEvent<HTMLDivElement>, status: TaskStatus) {
    event.preventDefault();
    const id = event.dataTransfer.getData('text/plain') || draggingTaskId;
    const task = tasks.find((item) => item.id === id);
    setDraggingTaskId(null);
    setDropTargetStatus(null);
    if (task && task.status !== status) update.mutate({ id: task.id, input: { status } });
  }

  function submit(event: FormEvent) {
    event.preventDefault();
    if (!title.trim()) return;
    const input: TaskInput = {
      title: title.trim(),
      description: description.trim() || undefined,
      priority,
      status: defaultStatus,
      dueAt: dueAt ? new Date(`${dueAt}T12:00:00`).toISOString() : undefined,
    };
    if (editingTask) update.mutate({ id: editingTask.id, input });
    else create.mutate(input);
  }

  useEffect(() => {
    if (page > totalPages) setPage(totalPages);
  }, [page, totalPages]);

  return (
    <main className="space-y-6 animate-fade-in" aria-labelledby="tasks-title">
      <header className="flex items-start justify-between gap-4">
        <div>
          <h1 id="tasks-title" className="flex items-center gap-2 text-2xl font-bold tracking-tight"><CheckSquare className="h-6 w-6 text-primary" />Tarefas</h1>
          <p className="text-muted-foreground">Organize e acompanhe suas tarefas operacionais.</p>
        </div>
        <div className="flex items-center gap-2">
          <div className="flex overflow-hidden rounded-md border">
            <Button type="button" variant={viewMode === 'kanban' ? 'default' : 'ghost'} size="sm" className="h-9 rounded-none px-3 text-xs" onClick={() => setViewMode('kanban')}><LayoutDashboard className="h-3.5 w-3.5" />Kanban</Button>
            <Button type="button" variant={viewMode === 'list' ? 'default' : 'ghost'} size="sm" className="h-9 rounded-none px-3 text-xs" onClick={() => setViewMode('list')}><List className="h-3.5 w-3.5" />Lista</Button>
          </div>
          <Button type="button" onClick={() => openCreate()} className="shrink-0"><Plus className="mr-1.5 h-4 w-4" />Nova tarefa</Button>
        </div>
      </header>

      <div className="flex flex-wrap items-center justify-between gap-3">
        {viewMode === 'list' ? (
          <Tabs value={statusFilter} onValueChange={(value) => changeFilter(setStatusFilter, value as StatusFilter)}>
            <TabsList>
              <TabsTrigger value="all" className="gap-1.5 text-xs">Todas{baseFilteredTasks.length > 0 && <span className="rounded-full bg-muted px-1.5 py-0.5 text-[10px] font-medium">{baseFilteredTasks.length}</span>}</TabsTrigger>
              {columns.map((column) => { const count = baseFilteredTasks.filter((task) => task.status === column.status).length; return <TabsTrigger key={column.status} value={column.status} className="gap-1.5 text-xs">{column.label}{count > 0 && <span className="rounded-full bg-muted px-1.5 py-0.5 text-[10px] font-medium">{count}</span>}</TabsTrigger>; })}
            </TabsList>
          </Tabs>
        ) : <span className="text-xs text-muted-foreground">{filteredTasks.length} tarefa{filteredTasks.length === 1 ? '' : 's'}</span>}
        <div className="flex flex-wrap items-center gap-2">
          <div className="relative w-full sm:w-56"><ListFilter className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" /><Input value={search} onChange={(event) => changeFilter(setSearch, event.target.value)} placeholder="Buscar tarefa…" className="pl-9" /></div>
          <Select value={priorityFilter} onValueChange={(value) => changeFilter(setPriorityFilter, value as 'all' | TaskPriority)}><SelectTrigger className="h-9 w-36 text-xs"><SelectValue placeholder="Prioridade" /></SelectTrigger><SelectContent><SelectItem value="all">Todas as prioridades</SelectItem><SelectItem value="low">Baixa</SelectItem><SelectItem value="medium">Média</SelectItem><SelectItem value="high">Alta</SelectItem><SelectItem value="urgent">Urgente</SelectItem></SelectContent></Select>
          <Select value={dateWindow} onValueChange={(value) => changeFilter(setDateWindow, value as DateWindow)}><SelectTrigger className="h-9 w-36 text-xs"><CalendarRange className="mr-1.5 h-3.5 w-3.5" /><SelectValue placeholder="Período" /></SelectTrigger><SelectContent><SelectItem value="today">Hoje</SelectItem><SelectItem value="week">Próximos 7 dias</SelectItem><SelectItem value="overdue">Atrasadas</SelectItem><SelectItem value="all">Todos os prazos</SelectItem></SelectContent></Select>
        </div>
      </div>

      {query.isPending && <Card><CardContent className="flex items-center justify-center p-12 text-sm text-muted-foreground">Carregando tarefas…</CardContent></Card>}
      {query.isError && <Card className="border-destructive/30"><CardContent className="p-6 text-sm text-destructive" role="alert">Não foi possível carregar as tarefas.</CardContent></Card>}

      {query.isSuccess && viewMode === 'kanban' && (
          <section className="grid min-h-[400px] grid-cols-1 gap-4 md:grid-cols-3" aria-label="Quadro de tarefas">
            {columns.map((column) => (
            <div key={column.status} className={cn('flex min-h-[300px] flex-col rounded-lg border-2 transition-colors', column.border, dropTargetStatus === column.status && 'border-primary bg-primary/5')} onDragOver={(event) => { event.preventDefault(); setDropTargetStatus(column.status); }} onDrop={(event) => handleDrop(event, column.status)} onDragLeave={() => setDropTargetStatus(null)}>
              <div className={`flex items-center justify-between rounded-t-md px-3 py-2 ${column.header}`}><span className="text-xs font-semibold uppercase tracking-wider">{column.label}</span><div className="flex items-center gap-1.5"><Badge variant="secondary" className="h-4 px-1.5 py-0 text-[10px]">{tasksByStatus[column.status].length}</Badge><Button type="button" size="icon" variant="ghost" className="h-5 w-5" onClick={(event) => { event.stopPropagation(); openCreate(column.status); }} title={`Nova tarefa em "${column.label}"`}><Plus className="h-3 w-3" /></Button></div></div>
              <div className="flex-1 space-y-2 p-2">
                {paginatedTasksByStatus[column.status].length === 0 ? <div className="flex h-24 items-center justify-center rounded border border-dashed text-xs text-muted-foreground">{dropTargetStatus === column.status && draggingTaskId ? 'Solte a tarefa aqui' : 'Nenhuma tarefa'}</div> : paginatedTasksByStatus[column.status].map((task) => <div key={task.id} draggable onDragStart={(event) => handleDragStart(event, task)} onDragEnd={() => { setDraggingTaskId(null); setDropTargetStatus(null); }} className={cn('cursor-grab active:cursor-grabbing', draggingTaskId === task.id && 'opacity-60')}><TaskCard task={task} onOpen={setDetailTask} onEdit={openEdit} onDelete={setDeleteId} onStatusChange={(id, status) => update.mutate({ id, input: { status } })} /></div>)}
              </div>
            </div>
          ))}
        </section>
      )}

      {query.isSuccess && viewMode === 'list' && (filteredTasks.length === 0 ? <div className="flex flex-col items-center justify-center rounded-lg border border-dashed py-16 text-center"><CheckSquare className="mb-3 h-10 w-10 text-muted-foreground/40" /><p className="text-sm font-medium text-muted-foreground">{dateWindow === 'today' && statusFilter === 'all' && priorityFilter === 'all' && !search.trim() ? 'Nenhuma tarefa prevista para hoje' : 'Nenhuma tarefa encontrada com esses filtros.'}</p><Button type="button" variant="outline" size="sm" className="mt-4" onClick={() => openCreate()}><Plus className="mr-1.5 h-4 w-4" />Criar tarefa</Button></div> : <div className="space-y-2">{paginatedTasks.map((task) => <TaskCard key={task.id} task={task} onOpen={setDetailTask} onEdit={openEdit} onDelete={setDeleteId} onStatusChange={(id, status) => update.mutate({ id, input: { status } })} />)}</div>)}
      {query.isSuccess && total > 0 && <div className="flex flex-col gap-3 border-t pt-4 sm:flex-row sm:items-center sm:justify-between"><p className="text-sm text-muted-foreground">Página {page} de {totalPages} · {total} tarefas</p><div className="flex items-center gap-2"><Button type="button" variant="outline" size="sm" onClick={() => setPage((value) => Math.max(1, value - 1))} disabled={page <= 1 || query.isFetching}><ChevronLeft className="mr-1 h-4 w-4" />Anterior</Button><Button type="button" variant="outline" size="sm" onClick={() => setPage((value) => Math.min(totalPages, value + 1))} disabled={page >= totalPages || query.isFetching}>Próxima<ChevronRight className="ml-1 h-4 w-4" /></Button></div></div>}

      <TaskDetailDrawer task={detailTask} onClose={() => setDetailTask(null)} onEdit={openEdit} onDelete={setDeleteId} onStatusChange={(id, status) => update.mutate({ id, input: { status } })} />

      <Dialog open={formOpen} onOpenChange={(open) => { if (!open) { setFormOpen(false); resetForm(); } }}><DialogContent><DialogHeader><DialogTitle>{editingTask ? 'Editar tarefa' : 'Nova tarefa'}</DialogTitle><DialogDescription>{editingTask ? 'Atualize as informações da tarefa.' : 'Preencha os dados para criar uma nova tarefa.'}</DialogDescription></DialogHeader><form className="grid gap-4" onSubmit={submit}><label className="grid gap-1.5 text-sm font-medium">Título<Input autoFocus value={title} onChange={(event) => setTitle(event.target.value)} placeholder="Ex.: acompanhar aluno" required /></label><div className="grid gap-4 sm:grid-cols-2"><label className="grid gap-1.5 text-sm font-medium">Prioridade<Select value={priority} onValueChange={(value) => setPriority(value as TaskPriority)}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent><SelectItem value="low">Baixa</SelectItem><SelectItem value="medium">Média</SelectItem><SelectItem value="high">Alta</SelectItem><SelectItem value="urgent">Urgente</SelectItem></SelectContent></Select></label><label className="grid gap-1.5 text-sm font-medium">Status<Select value={defaultStatus} onValueChange={(value) => setDefaultStatus(value as TaskStatus)}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent>{columns.map((column) => <SelectItem key={column.status} value={column.status}>{column.label}</SelectItem>)}</SelectContent></Select></label></div><label className="grid gap-1.5 text-sm font-medium">Prazo<Input type="date" value={dueAt} onChange={(event) => setDueAt(event.target.value)} /></label><label className="grid gap-1.5 text-sm font-medium">Descrição<Textarea value={description} onChange={(event) => setDescription(event.target.value)} placeholder="Descrição opcional" /></label><DialogFooter><Button type="button" variant="outline" onClick={() => { setFormOpen(false); resetForm(); }}>Cancelar</Button><Button type="submit" disabled={create.isPending || update.isPending}>{create.isPending || update.isPending ? 'Salvando…' : 'Salvar tarefa'}</Button></DialogFooter></form></DialogContent></Dialog>
      <AlertDialog open={Boolean(deleteId)} onOpenChange={(open) => { if (!open) setDeleteId(null); }}><AlertDialogContent><AlertDialogHeader><AlertDialogTitle>Remover tarefa?</AlertDialogTitle><AlertDialogDescription>Esta ação não pode ser desfeita. A tarefa será permanentemente removida.</AlertDialogDescription></AlertDialogHeader><AlertDialogFooter><AlertDialogCancel>Cancelar</AlertDialogCancel><AlertDialogAction className="bg-destructive text-destructive-foreground hover:bg-destructive/90" onClick={() => deleteId && remove.mutate(deleteId)}>{remove.isPending ? 'Removendo…' : 'Remover'}</AlertDialogAction></AlertDialogFooter></AlertDialogContent></AlertDialog>
    </main>
  );
}

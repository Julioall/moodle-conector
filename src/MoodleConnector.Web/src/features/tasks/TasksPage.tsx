import { useEffect, useMemo, useState, type DragEvent, type FormEvent, type MouseEvent } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Calendar,
  CalendarRange,
  Archive,
  ArrowRight,
  Check,
  CheckSquare,
  ChevronLeft,
  ChevronRight,
  Edit2,
  Flag,
  LayoutDashboard,
  List,
  ListFilter,
  MoreHorizontal,
  Plus,
  Trash2,
  X,
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
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuSeparator, DropdownMenuSub, DropdownMenuSubContent, DropdownMenuSubTrigger, DropdownMenuTrigger } from '@/components/ui/dropdown-menu';
import { Input } from '@/components/ui/input';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { Textarea } from '@/components/ui/textarea';
import { cn } from '@/lib/utils';
import { TaskDetailDrawer } from './components/TaskDetailDrawer';
import { tasksGateway, type Task, type TaskInput, type TaskPriority, type TaskStatus } from './tasks-gateway';
import { PlannerReferenceTags } from './PlannerReferenceTags';

type ViewMode = 'list' | 'kanban';
type StatusFilter = 'all' | TaskStatus;
type DateWindow = 'all' | 'today' | 'week' | 'overdue';
type ColumnPages = Record<TaskStatus, number>;

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

function formatStartDate(value?: string) {
  if (!value) return 'Sem início';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? 'Sem início' : date.toLocaleDateString('pt-BR');
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

function mutationErrorMessage(error: unknown, fallback: string) {
  return error instanceof Error && error.message ? error.message : fallback;
}

function TaskCard({
  task,
  onEdit,
  onDelete,
  onStatusChange,
  onOpen,
  selectionMode,
  selected,
  onSelect,
}: {
  task: Task;
  onEdit: (task: Task) => void;
  onDelete: (id: string) => void;
  onStatusChange: (id: string, status: TaskStatus) => void;
  onOpen: (task: Task) => void;
  selectionMode?: boolean;
  selected?: boolean;
  onSelect?: (id: string, selected: boolean) => void;
}) {
  const stopPropagation = (event: MouseEvent) => event.stopPropagation();

  return (
    <div className={cn('group cursor-pointer rounded-lg border bg-card p-4 shadow-sm transition-all hover:shadow-md', task.status === 'done' && 'opacity-70')} onClick={() => onOpen(task)}>
      <div className="flex items-start gap-3">
          <input
            type="checkbox"
            checked={selectionMode ? selected : task.status === 'done'}
            onChange={(event) => selectionMode ? onSelect?.(task.id, event.target.checked) : onStatusChange(task.id, event.target.checked ? 'done' : 'todo')}
            onClick={stopPropagation}
            className="mt-0.5 h-4 w-4 cursor-pointer rounded accent-primary"
            aria-label={selectionMode ? `${selected ? 'Desmarcar' : 'Selecionar'} ${task.title}` : task.status === 'done' ? 'Marcar como pendente' : 'Marcar como concluída'}
          />
          <div className="min-w-0 flex-1">
            <p className={cn('line-clamp-2 text-sm font-medium leading-snug', task.status === 'done' && 'text-muted-foreground line-through')}>{task.title}</p>
            {task.description && <p className="mt-1 line-clamp-2 text-xs text-muted-foreground">{task.description}</p>}
            <PlannerReferenceTags references={task.references} compact />
            <div className="mt-2 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs">
              <span className={cn('shrink-0 rounded-full px-2 py-0.5 text-[11px] font-medium', priorityStyles[task.priority])}>{priorityLabels[task.priority]}</span>
              <span className="inline-flex items-center gap-1 text-muted-foreground"><Calendar className="h-3 w-3" />Início: {formatStartDate(task.startAt)}</span>
              <span className={cn('inline-flex items-center gap-1', isOverdue(task) ? 'text-destructive' : 'text-muted-foreground')}>
                <Calendar className="h-3 w-3" />Prazo: {formatDueDate(task.dueAt)}
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

function TaskColumnMenu({
  column,
  tasks,
  onUpdate,
  onDelete,
  onArchive,
}: {
  column: (typeof columns)[number];
  tasks: Task[];
  onUpdate: (tasks: Task[], status: TaskStatus) => void;
  onDelete: (ids: string[]) => void;
  onArchive: (status: TaskStatus) => void;
}) {
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button type="button" size="icon" variant="ghost" className="h-7 w-7 shrink-0" aria-label={`Opções da coluna ${column.label}`} title={`Opções da coluna ${column.label}`}>
          <MoreHorizontal className="h-4 w-4" />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-56">
        <DropdownMenuItem disabled={tasks.length === 0 || column.status === 'done'} onSelect={() => onUpdate(tasks, 'done')}>
          <Check className="mr-2 h-4 w-4" />
          Marcar todos como feitos
        </DropdownMenuItem>
        <DropdownMenuSub>
          <DropdownMenuSubTrigger disabled={tasks.length === 0}>
            <ArrowRight className="mr-2 h-4 w-4" />
            Mover todos
          </DropdownMenuSubTrigger>
          <DropdownMenuSubContent>
            {columns.filter((target) => target.status !== column.status).map((target) => (
              <DropdownMenuItem key={target.status} onSelect={() => onUpdate(tasks, target.status)}>
                {target.label}
              </DropdownMenuItem>
            ))}
          </DropdownMenuSubContent>
        </DropdownMenuSub>
        <DropdownMenuSeparator />
        <DropdownMenuItem onSelect={() => onArchive(column.status)}>
          <Archive className="mr-2 h-4 w-4" />
          Arquivar coluna
        </DropdownMenuItem>
        <DropdownMenuItem disabled={tasks.length === 0} className="text-destructive focus:text-destructive" onSelect={() => onDelete(tasks.map((task) => task.id))}>
          <Trash2 className="mr-2 h-4 w-4" />
          Apagar todos
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}

export function TasksPage() {
  const client = useQueryClient();
  const [viewMode, setViewMode] = useState<ViewMode>('kanban');
  const [statusFilter, setStatusFilter] = useState<StatusFilter>('all');
  const [priorityFilter, setPriorityFilter] = useState<'all' | TaskPriority>('all');
  const [dateWindow, setDateWindow] = useState<DateWindow>('all');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [columnPages, setColumnPages] = useState<ColumnPages>({ todo: 1, in_progress: 1, done: 1 });
  const [formOpen, setFormOpen] = useState(false);
  const [detailTask, setDetailTask] = useState<Task | null>(null);
  const [draggingTaskId, setDraggingTaskId] = useState<string | null>(null);
  const [dropTargetStatus, setDropTargetStatus] = useState<TaskStatus | null>(null);
  const [editingTask, setEditingTask] = useState<Task | null>(null);
  const [deleteIds, setDeleteIds] = useState<string[]>([]);
  const [selectionMode, setSelectionMode] = useState(false);
  const [selectedTaskIds, setSelectedTaskIds] = useState<Set<string>>(() => new Set());
  const [archivedColumns, setArchivedColumns] = useState<Set<TaskStatus>>(() => new Set());
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [priority, setPriority] = useState<TaskPriority>('medium');
  const [startAt, setStartAt] = useState('');
  const [dueAt, setDueAt] = useState('');
  const [defaultStatus, setDefaultStatus] = useState<TaskStatus>('todo');
  const [taskActionError, setTaskActionError] = useState<string | null>(null);

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
  const visibleColumns = useMemo(() => columns.filter((column) => !archivedColumns.has(column.status)), [archivedColumns]);
  const pageSize = 20;
  const total = filteredTasks.length;
  const totalPages = Math.max(1, Math.ceil(total / pageSize));
  const paginatedTasks = useMemo(() => filteredTasks.slice((page - 1) * pageSize, page * pageSize), [filteredTasks, page]);
  const refreshPlannerCounters = () => void client.invalidateQueries({ queryKey: ['app', 'dashboard', 'summary'] });

  const create = useMutation({
    mutationFn: (input: TaskInput) => tasksGateway.create(input),
    onSuccess: () => { setTaskActionError(null); setFormOpen(false); resetForm(); void client.invalidateQueries({ queryKey: ['app', 'tasks'] }); refreshPlannerCounters(); },
    onError: (error) => setTaskActionError(mutationErrorMessage(error, 'Não foi possível criar a tarefa.')),
  });
  const update = useMutation({
    mutationFn: ({ id, input }: { id: string; input: TaskInput }) => tasksGateway.update(id, input),
    onSuccess: () => { setTaskActionError(null); setFormOpen(false); resetForm(); void client.invalidateQueries({ queryKey: ['app', 'tasks'] }); refreshPlannerCounters(); },
    onError: (error) => setTaskActionError(mutationErrorMessage(error, 'Não foi possível atualizar a tarefa.')),
  });
  const bulkUpdate = useMutation({
    mutationFn: async ({ ids, status }: { ids: string[]; status: TaskStatus }) => {
      const results = [];
      for (let index = 0; index < ids.length; index += 4) {
        results.push(...await Promise.all(ids.slice(index, index + 4).map((id) => tasksGateway.update(id, { status }))));
      }
      return results;
    },
    onSuccess: () => { setTaskActionError(null); setSelectedTaskIds(new Set()); setSelectionMode(false); void client.invalidateQueries({ queryKey: ['app', 'tasks'] }); refreshPlannerCounters(); },
    onError: (error) => setTaskActionError(mutationErrorMessage(error, 'Não foi possível atualizar os itens selecionados.')),
  });
  const remove = useMutation({
    mutationFn: tasksGateway.remove,
    onSuccess: () => { setTaskActionError(null); setDeleteIds([]); setDetailTask(null); void client.invalidateQueries({ queryKey: ['app', 'tasks'] }); refreshPlannerCounters(); },
    onError: (error) => setTaskActionError(mutationErrorMessage(error, 'Não foi possível remover a tarefa.')),
  });
  const removeMany = useMutation({
    mutationFn: tasksGateway.removeMany,
    onSuccess: () => { setTaskActionError(null); setDeleteIds([]); setSelectedTaskIds(new Set()); setSelectionMode(false); setDetailTask(null); void client.invalidateQueries({ queryKey: ['app', 'tasks'] }); refreshPlannerCounters(); },
    onError: (error) => setTaskActionError(mutationErrorMessage(error, 'Nao foi possivel remover as tarefas selecionadas.')),
  });

  function resetForm() {
    setEditingTask(null);
    setTitle('');
    setDescription('');
    setPriority('medium');
    setStartAt('');
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
    setStartAt(dateInputValue(task.startAt));
    setDueAt(dateInputValue(task.dueAt));
    setDefaultStatus(task.status);
    setFormOpen(true);
  }

  function changeFilter<T>(setter: (value: T) => void, value: T) {
    setter(value);
    setPage(1);
    setColumnPages({ todo: 1, in_progress: 1, done: 1 });
    clearTaskSelection();
  }

  function toggleAllFilteredSelection(checked: boolean) {
    setSelectionMode(true);
    setSelectedTaskIds(checked ? new Set(filteredTasks.map((task) => task.id)) : new Set());
  }

  function toggleTaskSelection(id: string, selected: boolean) {
    setSelectedTaskIds((current) => {
      const next = new Set(current);
      if (selected) next.add(id);
      else next.delete(id);
      return next;
    });
  }

  function clearTaskSelection() {
    setSelectedTaskIds(new Set());
    setSelectionMode(false);
  }

  function completeSelectedTasks() {
    if (selectedTaskIds.size === 0) return;
    bulkUpdate.mutate({ ids: Array.from(selectedTaskIds), status: 'done' });
  }

  function updateColumnTasks(tasksOnColumn: Task[], status: TaskStatus) {
    if (tasksOnColumn.length === 0) return;
    bulkUpdate.mutate({ ids: tasksOnColumn.map((task) => task.id), status });
  }

  function archiveColumn(status: TaskStatus) {
    setArchivedColumns((current) => new Set(current).add(status));
    setSelectedTaskIds((current) => {
      const next = new Set(current);
      tasksByStatus[status].forEach((task) => next.delete(task.id));
      return next;
    });
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
      startAt: startAt ? new Date(`${startAt}T12:00:00`).toISOString() : undefined,
      dueAt: dueAt ? new Date(`${dueAt}T12:00:00`).toISOString() : undefined,
    };
    if (editingTask) update.mutate({ id: editingTask.id, input });
    else create.mutate(input);
  }

  useEffect(() => {
    if (page > totalPages) setPage(totalPages);
  }, [page, totalPages]);

  useEffect(() => {
    setColumnPages((current) => {
      const next = { ...current };
      let changed = false;
      columns.forEach((column) => {
        const totalColumnPages = Math.max(1, Math.ceil(tasksByStatus[column.status].length / pageSize));
        const nextPage = Math.min(current[column.status], totalColumnPages);
        if (nextPage !== current[column.status]) { next[column.status] = nextPage; changed = true; }
      });
      return changed ? next : current;
    });
  }, [tasksByStatus]);

  return (
    <main className="flex min-h-0 flex-1 flex-col gap-6 animate-fade-in" aria-labelledby="tasks-title">
      <header className="flex items-start justify-between gap-4">
        <div>
          <h1 id="tasks-title" className="flex items-center gap-2 text-2xl font-bold tracking-tight"><CheckSquare className="h-6 w-6 text-primary" />Tarefas</h1>
          <p className="text-muted-foreground">Organize e acompanhe suas tarefas operacionais.</p>
        </div>
        <div className="flex items-center gap-2">
          {selectionMode ? <div className="flex items-center gap-1 rounded-md border bg-muted/40 p-1">
            <span className="px-2 text-xs text-muted-foreground">{selectedTaskIds.size} selecionada{selectedTaskIds.size === 1 ? '' : 's'}</span>
            <Button type="button" variant="ghost" size="icon" className="h-7 w-7" title="Concluir selecionadas" aria-label="Concluir selecionadas" disabled={selectedTaskIds.size === 0 || bulkUpdate.isPending} onClick={completeSelectedTasks}><Check className="h-4 w-4" /></Button>
            <Button type="button" variant="ghost" size="icon" className="h-7 w-7 text-muted-foreground hover:text-destructive" title="Remover selecionadas" aria-label="Remover selecionadas" disabled={selectedTaskIds.size === 0 || removeMany.isPending} onClick={() => setDeleteIds(Array.from(selectedTaskIds))}><Trash2 className="h-4 w-4" /></Button>
            <Button type="button" variant="ghost" size="icon" className="h-7 w-7" title="Cancelar seleção" aria-label="Cancelar seleção" onClick={clearTaskSelection}><X className="h-4 w-4" /></Button>
          </div> : <Button type="button" variant="outline" size="sm" onClick={() => setSelectionMode(true)}>Selecionar</Button>}
          <div className="flex overflow-hidden rounded-md border">
            <Button type="button" variant={viewMode === 'kanban' ? 'default' : 'ghost'} size="sm" className="h-9 rounded-none px-3 text-xs" onClick={() => setViewMode('kanban')}><LayoutDashboard className="h-3.5 w-3.5" />Kanban</Button>
            <Button type="button" variant={viewMode === 'list' ? 'default' : 'ghost'} size="sm" className="h-9 rounded-none px-3 text-xs" onClick={() => setViewMode('list')}><List className="h-3.5 w-3.5" />Lista</Button>
          </div>
          <Button type="button" onClick={() => openCreate()} className="shrink-0"><Plus className="mr-1.5 h-4 w-4" />Nova tarefa</Button>
        </div>
      </header>

      <div className="flex flex-wrap items-center justify-between gap-3">
        {selectionMode ? <label className="flex h-9 items-center gap-2 rounded-md border px-3 text-xs font-medium"><input type="checkbox" checked={filteredTasks.length > 0 && filteredTasks.every((task) => selectedTaskIds.has(task.id))} disabled={filteredTasks.length === 0} onChange={(event) => toggleAllFilteredSelection(event.target.checked)} className="h-3.5 w-3.5 cursor-pointer rounded accent-primary" aria-label="Selecionar todos" /><span>Selecionar todos</span></label> : <span className="text-xs text-muted-foreground">{filteredTasks.length} tarefa{filteredTasks.length === 1 ? '' : 's'}</span>}
        {viewMode === 'list' ? (
          <Tabs value={statusFilter} onValueChange={(value) => changeFilter(setStatusFilter, value as StatusFilter)}>
            <TabsList>
              <TabsTrigger value="all" className="gap-1.5 text-xs">Todas{baseFilteredTasks.length > 0 && <span className="rounded-full bg-muted px-1.5 py-0.5 text-[10px] font-medium">{baseFilteredTasks.length}</span>}</TabsTrigger>
              {columns.map((column) => { const count = baseFilteredTasks.filter((task) => task.status === column.status).length; return <TabsTrigger key={column.status} value={column.status} className="gap-1.5 text-xs">{column.label}{count > 0 && <span className="rounded-full bg-muted px-1.5 py-0.5 text-[10px] font-medium">{count}</span>}</TabsTrigger>; })}
            </TabsList>
          </Tabs>
        ) : null}
        <div className="flex flex-wrap items-center gap-2">
          <div className="relative w-full sm:w-56"><ListFilter className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" /><Input value={search} onChange={(event) => changeFilter(setSearch, event.target.value)} placeholder="Buscar tarefa…" className="pl-9" /></div>
          <Select value={priorityFilter} onValueChange={(value) => changeFilter(setPriorityFilter, value as 'all' | TaskPriority)}><SelectTrigger className="h-9 w-36 text-xs"><Flag className="mr-1.5 h-3.5 w-3.5" /><SelectValue placeholder="Prioridade" /></SelectTrigger><SelectContent><SelectItem value="all">Todas as prioridades</SelectItem><SelectItem value="low">Baixa</SelectItem><SelectItem value="medium">Média</SelectItem><SelectItem value="high">Alta</SelectItem><SelectItem value="urgent">Urgente</SelectItem></SelectContent></Select>
          <Select value={dateWindow} onValueChange={(value) => changeFilter(setDateWindow, value as DateWindow)}><SelectTrigger className="h-9 w-36 text-xs"><CalendarRange className="mr-1.5 h-3.5 w-3.5" /><SelectValue placeholder="Período" /></SelectTrigger><SelectContent><SelectItem value="today">Hoje</SelectItem><SelectItem value="week">Próximos 7 dias</SelectItem><SelectItem value="overdue">Atrasadas</SelectItem><SelectItem value="all">Todos os prazos</SelectItem></SelectContent></Select>
        </div>
      </div>

      {archivedColumns.size > 0 && <div className="flex items-center justify-between gap-3 rounded-md border border-dashed px-3 py-2 text-xs text-muted-foreground"><span>{archivedColumns.size} coluna{archivedColumns.size === 1 ? '' : 's'} arquivada{archivedColumns.size === 1 ? '' : 's'}</span><Button type="button" variant="ghost" size="sm" className="h-7 px-2 text-xs" onClick={() => setArchivedColumns(new Set())}>Restaurar colunas</Button></div>}

      {query.isPending && <Card><CardContent className="flex items-center justify-center p-12 text-sm text-muted-foreground">Carregando tarefas…</CardContent></Card>}
      {query.isError && <Card className="border-destructive/30"><CardContent className="p-6 text-sm text-destructive" role="alert">Não foi possível carregar as tarefas.</CardContent></Card>}
      {taskActionError && <Card className="border-destructive/30"><CardContent className="p-4 text-sm text-destructive" role="alert">{taskActionError}</CardContent></Card>}

      {query.isSuccess && viewMode === 'kanban' && (
          <section className="grid min-h-[400px] flex-1 grid-cols-1 gap-4 md:grid-cols-3" aria-label="Quadro de tarefas">
            {visibleColumns.map((column) => {
              const columnTasks = tasksByStatus[column.status];
              const columnTotalPages = Math.max(1, Math.ceil(columnTasks.length / pageSize));
              const columnPage = Math.min(columnPages[column.status], columnTotalPages);
              const columnPageTasks = columnTasks.slice((columnPage - 1) * pageSize, columnPage * pageSize);
              return <div key={column.status} className={cn('flex min-h-[300px] flex-col rounded-lg border-2 transition-colors', column.border, dropTargetStatus === column.status && 'border-primary bg-primary/5')} onDragOver={(event) => { event.preventDefault(); setDropTargetStatus(column.status); }} onDrop={(event) => handleDrop(event, column.status)} onDragLeave={() => setDropTargetStatus(null)}>
                <div className={`flex items-center justify-between rounded-t-md px-3 py-2 ${column.header}`}><div className="flex min-w-0 items-center gap-2"><span className="text-xs font-semibold uppercase tracking-wider">{column.label}</span><Badge variant="secondary" className="h-4 px-1.5 py-0 text-[10px]">{columnTasks.length}</Badge></div><div className="flex shrink-0 items-center gap-0.5"><Button type="button" size="icon" variant="ghost" className="h-7 w-7" onClick={(event) => { event.stopPropagation(); openCreate(column.status); }} title={`Nova tarefa em "${column.label}"`} aria-label={`Nova tarefa em "${column.label}"`}><Plus className="h-4 w-4" /></Button><TaskColumnMenu column={column} tasks={columnTasks} onUpdate={updateColumnTasks} onDelete={(ids) => setDeleteIds(ids)} onArchive={archiveColumn} /></div></div>
                <div className="flex-1 space-y-2 p-2">{columnPageTasks.length === 0 ? <div className="flex min-h-0 flex-1 items-center justify-center rounded border border-dashed text-xs text-muted-foreground">{dropTargetStatus === column.status && draggingTaskId ? 'Solte a tarefa aqui' : 'Nenhuma tarefa'}</div> : columnPageTasks.map((task) => <div key={task.id} draggable={!selectionMode} onDragStart={(event) => handleDragStart(event, task)} onDragEnd={() => { setDraggingTaskId(null); setDropTargetStatus(null); }} className={cn('cursor-grab active:cursor-grabbing', draggingTaskId === task.id && 'opacity-60')}><TaskCard task={task} onOpen={setDetailTask} onEdit={openEdit} onDelete={(id) => setDeleteIds([id])} onStatusChange={(id, status) => update.mutate({ id, input: { status } })} selectionMode={selectionMode} selected={selectedTaskIds.has(task.id)} onSelect={toggleTaskSelection} /></div>)}</div>
                {columnTasks.length > 0 && <div className="flex flex-wrap items-center justify-between gap-2 border-t px-2 py-2 text-[11px] text-muted-foreground"><span>Página {columnPage} de {columnTotalPages}</span><div className="flex gap-1"><Button type="button" variant="ghost" size="sm" className="h-7 px-2 text-[11px]" onClick={() => setColumnPages((current) => ({ ...current, [column.status]: Math.max(1, current[column.status] - 1) }))} disabled={columnPage <= 1}>Anterior</Button><Button type="button" variant="ghost" size="sm" className="h-7 px-2 text-[11px]" onClick={() => setColumnPages((current) => ({ ...current, [column.status]: Math.min(columnTotalPages, current[column.status] + 1) }))} disabled={columnPage >= columnTotalPages}>Próxima</Button></div></div>}
              </div>;
            })}
        </section>
      )}

      {query.isSuccess && viewMode === 'list' && (filteredTasks.length === 0 ? <div className="flex flex-col items-center justify-center rounded-lg border border-dashed py-16 text-center"><CheckSquare className="mb-3 h-10 w-10 text-muted-foreground/40" /><p className="text-sm font-medium text-muted-foreground">{dateWindow === 'today' && statusFilter === 'all' && priorityFilter === 'all' && !search.trim() ? 'Nenhuma tarefa prevista para hoje' : 'Nenhuma tarefa encontrada com esses filtros.'}</p><Button type="button" variant="outline" size="sm" className="mt-4" onClick={() => openCreate()}><Plus className="mr-1.5 h-4 w-4" />Criar tarefa</Button></div> : <div className="space-y-2">{paginatedTasks.map((task) => <TaskCard key={task.id} task={task} onOpen={setDetailTask} onEdit={openEdit} onDelete={(id) => setDeleteIds([id])} onStatusChange={(id, status) => update.mutate({ id, input: { status } })} selectionMode={selectionMode} selected={selectedTaskIds.has(task.id)} onSelect={toggleTaskSelection} />)}</div>)}
      {query.isSuccess && viewMode === 'list' && total > 0 && <div className="flex flex-col gap-3 border-t pt-4 sm:flex-row sm:items-center sm:justify-between"><p className="text-sm text-muted-foreground">Página {page} de {totalPages} · {total} tarefas</p><div className="flex items-center gap-2"><Button type="button" variant="outline" size="sm" onClick={() => setPage((value) => Math.max(1, value - 1))} disabled={page <= 1 || query.isFetching}><ChevronLeft className="mr-1 h-4 w-4" />Anterior</Button><Button type="button" variant="outline" size="sm" onClick={() => setPage((value) => Math.min(totalPages, value + 1))} disabled={page >= totalPages || query.isFetching}>Próxima<ChevronRight className="ml-1 h-4 w-4" /></Button></div></div>}

      <TaskDetailDrawer task={detailTask} onClose={() => setDetailTask(null)} onEdit={openEdit} onDelete={(id) => setDeleteIds([id])} onStatusChange={(id, status) => update.mutate({ id, input: { status } })} />

      <Dialog open={formOpen} onOpenChange={(open) => { if (!open) { setFormOpen(false); resetForm(); } }}><DialogContent><DialogHeader><DialogTitle>{editingTask ? 'Editar tarefa' : 'Nova tarefa'}</DialogTitle><DialogDescription>{editingTask ? 'Atualize as informações da tarefa.' : 'Preencha os dados para criar uma nova tarefa.'}</DialogDescription></DialogHeader><form className="grid gap-4" onSubmit={submit}><label className="grid gap-1.5 text-sm font-medium">Título<Input autoFocus value={title} onChange={(event) => setTitle(event.target.value)} placeholder="Ex.: acompanhar aluno" required /></label><div className="grid gap-4 sm:grid-cols-2"><label className="grid gap-1.5 text-sm font-medium">Prioridade<Select value={priority} onValueChange={(value) => setPriority(value as TaskPriority)}><SelectTrigger aria-label="Prioridade"><SelectValue /></SelectTrigger><SelectContent><SelectItem value="low">Baixa</SelectItem><SelectItem value="medium">Média</SelectItem><SelectItem value="high">Alta</SelectItem><SelectItem value="urgent">Urgente</SelectItem></SelectContent></Select></label><label className="grid gap-1.5 text-sm font-medium">Status<Select value={defaultStatus} onValueChange={(value) => setDefaultStatus(value as TaskStatus)}><SelectTrigger aria-label="Status"><SelectValue /></SelectTrigger><SelectContent>{columns.map((column) => <SelectItem key={column.status} value={column.status}>{column.label}</SelectItem>)}</SelectContent></Select></label></div><div className="grid gap-4 sm:grid-cols-2"><label className="grid gap-1.5 text-sm font-medium">Data de início<Input type="date" value={startAt} onChange={(event) => setStartAt(event.target.value)} /></label><label className="grid gap-1.5 text-sm font-medium">Prazo<Input type="date" value={dueAt} onChange={(event) => setDueAt(event.target.value)} /></label></div><label className="grid gap-1.5 text-sm font-medium">Descrição<Textarea value={description} onChange={(event) => setDescription(event.target.value)} placeholder="Descrição opcional" /></label><DialogFooter><Button type="button" variant="outline" onClick={() => { setFormOpen(false); resetForm(); }}>Cancelar</Button><Button type="submit" disabled={create.isPending || update.isPending}>{create.isPending || update.isPending ? 'Salvando…' : 'Salvar tarefa'}</Button></DialogFooter></form></DialogContent></Dialog>
      <AlertDialog open={deleteIds.length > 0} onOpenChange={(open) => { if (!open) setDeleteIds([]); }}><AlertDialogContent><AlertDialogHeader><AlertDialogTitle>{deleteIds.length > 1 ? 'Remover tarefas selecionadas?' : 'Remover tarefa?'}</AlertDialogTitle><AlertDialogDescription>Esta acao nao pode ser desfeita. {deleteIds.length > 1 ? `As ${deleteIds.length} tarefas selecionadas` : 'A tarefa'} serao permanentemente removidas.</AlertDialogDescription></AlertDialogHeader><AlertDialogFooter><AlertDialogCancel>Cancelar</AlertDialogCancel><AlertDialogAction className="bg-destructive text-destructive-foreground hover:bg-destructive/90" onClick={() => { if (deleteIds.length > 1) removeMany.mutate(deleteIds); else if (deleteIds[0]) remove.mutate(deleteIds[0]); }}>{remove.isPending || removeMany.isPending ? 'Removendo...' : 'Remover'}</AlertDialogAction></AlertDialogFooter></AlertDialogContent></AlertDialog>
    </main>
  );
}

import { FormEvent, useState } from 'react';
import { authGateway } from './auth-gateway';
import './auth-page.css';

type Mode = 'login' | 'register';

export function AuthPage() {
  const initialParams = new URLSearchParams(window.location.search);
  const [mode, setMode] = useState<Mode>(initialParams.get('tab') === 'register' ? 'register' : 'login');
  const [name, setName] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState(initialParams.get('error') ?? '');
  const [pending, setPending] = useState(false);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setError('');
    setPending(true);
    try {
      if (mode === 'register') await authGateway.register(name, email, password);
      else await authGateway.login(email, password);
      // Keep the user inside the V2 SPA after the shared account cookie is issued.
      window.location.assign('/');
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Não foi possível concluir a operação.');
    } finally {
      setPending(false);
    }
  };

  return <main className="auth-page">
    <section className="auth-brand-panel" aria-label="Moodle Connector">
      <div className="brand-mark">M</div>
      <p className="auth-kicker">Moodle Connector</p>
      <h1>Central de tutoria</h1>
      <p>Organize cursos, alunos e acompanhamentos em um espaço operacional simples.</p>
    </section>
    <section className="auth-card" aria-labelledby="auth-title">
      <div className="auth-card-header">
        <span className="auth-label">App operacional</span>
        <h2 id="auth-title">{mode === 'login' ? 'Entrar na sua conta' : 'Criar sua conta'}</h2>
        <p>{mode === 'login' ? 'Acesse seu espaço de acompanhamento.' : 'Comece a configurar seu espaço Moodle Connector.'}</p>
      </div>
      <div className="auth-tabs" role="tablist" aria-label="Autenticação">
        <button type="button" role="tab" aria-selected={mode === 'login'} className={mode === 'login' ? 'active' : ''} onClick={() => { setMode('login'); setError(''); }}>Entrar</button>
        <button type="button" role="tab" aria-selected={mode === 'register'} className={mode === 'register' ? 'active' : ''} onClick={() => { setMode('register'); setError(''); }}>Criar conta</button>
      </div>
      <form className="auth-form" onSubmit={submit}>
        {mode === 'register' && <label>Nome<input required value={name} onChange={event => setName(event.target.value)} autoComplete="name" /></label>}
        <label>E-mail<input required type="email" value={email} onChange={event => setEmail(event.target.value)} autoComplete="email" /></label>
        <label>Senha<input required minLength={12} type="password" value={password} onChange={event => setPassword(event.target.value)} autoComplete={mode === 'login' ? 'current-password' : 'new-password'} /><small>Mínimo de 12 caracteres.</small></label>
        {error && <p className="auth-error" role="alert">{error}</p>}
        <button className="auth-submit" type="submit" disabled={pending}>{pending ? 'Aguarde…' : mode === 'login' ? 'Entrar' : 'Criar conta'}</button>
      </form>
    </section>
  </main>;
}


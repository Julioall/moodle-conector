export type MessagePreferences = { sendOnEnter: boolean };

export const MESSAGE_PREFERENCES_STORAGE_KEY = 'app:message-preferences';
const MESSAGE_PREFERENCES_UPDATED_EVENT = 'app:message-preferences-updated';

export function getStoredMessagePreferences(): MessagePreferences {
  if (typeof window === 'undefined') return { sendOnEnter: false };
  try {
    const parsed = JSON.parse(window.localStorage.getItem(MESSAGE_PREFERENCES_STORAGE_KEY) ?? '{}') as { sendOnEnter?: unknown };
    return { sendOnEnter: parsed.sendOnEnter === true };
  } catch {
    return { sendOnEnter: false };
  }
}

export function saveMessagePreferences(preferences: MessagePreferences) {
  if (typeof window === 'undefined') return;
  try {
    window.localStorage.setItem(MESSAGE_PREFERENCES_STORAGE_KEY, JSON.stringify(preferences));
    window.dispatchEvent(new CustomEvent(MESSAGE_PREFERENCES_UPDATED_EVENT));
  } catch {
    // Local preferences are best-effort in restricted browser contexts.
  }
}

export function subscribeToMessagePreferences(listener: (preferences: MessagePreferences) => void) {
  if (typeof window === 'undefined') return () => undefined;
  const handleUpdate = () => listener(getStoredMessagePreferences());
  window.addEventListener(MESSAGE_PREFERENCES_UPDATED_EVENT, handleUpdate);
  window.addEventListener('storage', handleUpdate);
  return () => {
    window.removeEventListener(MESSAGE_PREFERENCES_UPDATED_EVENT, handleUpdate);
    window.removeEventListener('storage', handleUpdate);
  };
}

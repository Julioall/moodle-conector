interface LogoProps {
  className?: string;
}

/** Official Moodle Conector wordmark. */
export function MoodleConnectorLogo({ className }: LogoProps) {
  return <img src="/logo.png" alt="Moodle Conector" className={className} />;
}

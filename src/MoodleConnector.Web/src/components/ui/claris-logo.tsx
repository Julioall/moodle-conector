/**
 * Claris brand components.
 * Both use `currentColor` so they adopt the text color of their container.
 * Use Tailwind text utilities (e.g. `text-primary`, `text-sidebar-foreground`)
 * on the parent or directly on the component to set the color.
 */

interface LogoProps {
  className?: string;
}

function ClarisSparklesSymbol({ className }: LogoProps) {
  return (
    <svg
      viewBox="36 28 48 51"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
      className={className}
      aria-hidden="true"
    >
      <path
        d="M60 30
       L64 48
       L82 52
       L64 56
       L60 74
       L56 56
       L38 52
       L56 48
       Z"
        fill="currentColor"
      />
      <path
        d="M76 34
       L77.5 39
       L82.5 40.5
       L77.5 42
       L76 47
       L74.5 42
       L69.5 40.5
       L74.5 39
       Z"
        fill="currentColor"
      />
      <path
        d="M44 64
       L45.5 69
       L50.5 70.5
       L45.5 72
       L44 77
       L42.5 72
       L37.5 70.5
       L42.5 69
       Z"
        fill="currentColor"
      />
    </svg>
  );
}

/** Full wordmark — symbol + "Claris" text. Use on login and wide areas. */
export function ClarisLogo({ className }: LogoProps) {
  return (
    <svg
      viewBox="0 0 280 80"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
      className={className}
      aria-label="Claris"
      role="img"
    >
      <svg x="8" y="8" width="64" height="64" viewBox="36 28 48 51" fill="none" aria-hidden="true">
        <path
          d="M60 30
       L64 48
       L82 52
       L64 56
       L60 74
       L56 56
       L38 52
       L56 48
       Z"
          fill="currentColor"
        />
        <path
          d="M76 34
       L77.5 39
       L82.5 40.5
       L77.5 42
       L76 47
       L74.5 42
       L69.5 40.5
       L74.5 39
       Z"
          fill="currentColor"
        />
        <path
          d="M44 64
       L45.5 69
       L50.5 70.5
       L45.5 72
       L44 77
       L42.5 72
       L37.5 70.5
       L42.5 69
       Z"
          fill="currentColor"
        />
      </svg>
      <text
        x="80"
        y="50"
        fontFamily="Inter, Arial, sans-serif"
        fontSize="32"
        fontWeight="600"
        fill="currentColor"
        letterSpacing="0.5"
      >
        Claris
      </text>
    </svg>
  );
}

/** Icon only — circle symbol. Use on tabs, collapsed sidebar, favicon contexts. */
export function ClarisIcon({ className }: LogoProps) {
  return <ClarisSparklesSymbol className={className} />;
}
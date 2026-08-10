import { cn } from '@/lib/utils';

interface SpinnerProps {
  className?: string;
  onAccent?: boolean;
}

export function Spinner({ className, onAccent = false }: SpinnerProps) {
  return (
    <svg
      viewBox="34 26 52 55"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
      data-testid="spinner"
      className={cn(
        'inline-block',
        onAccent ? 'text-black' : 'text-primary',
        className
      )}
      aria-hidden="true"
    >
      <g style={{ transformOrigin: '60px 52px' }}>
        <animateTransform
          attributeName="transform"
          type="scale"
          values="1;1.08;1"
          dur="1.7s"
          repeatCount="indefinite"
        />
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
      </g>

      <g style={{ transformOrigin: '76px 40px' }}>
        <animateTransform
          attributeName="transform"
          type="scale"
          values="1;1.12;1"
          dur="1.25s"
          repeatCount="indefinite"
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
      </g>

      <g style={{ transformOrigin: '44px 70px' }}>
        <animateTransform
          attributeName="transform"
          type="scale"
          values="1;1.1;1"
          dur="1.4s"
          repeatCount="indefinite"
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
      </g>
    </svg>
  );
}
export type ControlMode = "keyboard" | "joystick";

export type Handedness = "left" | "right";

export type ConnectionStatus = "disconnected" | "connecting" | "connected" | "error";

export interface MouseInputEvent {
  type: "mouse";
  action: "move" | "click" | "dblclick" | "down" | "up" | "scroll";
  x: number; // 0-1 normalized
  y: number; // 0-1 normalized
  button?: number;
  deltaX?: number;
  deltaY?: number;
}

export interface KeyboardInputEvent {
  type: "keyboard";
  action: "down" | "up";
  key: string;
  code: string;
  modifiers: {
    ctrl: boolean;
    alt: boolean;
    shift: boolean;
    meta: boolean;
  };
}

export type InputEvent =
  | MouseInputEvent
  | KeyboardInputEvent;

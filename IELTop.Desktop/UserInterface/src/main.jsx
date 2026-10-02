import React from 'react'
import { createRoot } from 'react-dom/client'
import { MantineProvider, createTheme } from '@mantine/core'
import '@mantine/core/styles.css'
import App from './App.jsx'
import './styles.css'

// The app's own look: a compact, square, red accented theme over Mantine.
const theme = createTheme({
  primaryColor: 'ieltop',
  defaultRadius: 'sm',
  fontFamily: '"Segoe UI", system-ui, -apple-system, "Helvetica Neue", Arial, sans-serif',
  colors: {
    ieltop: [
      '#fdecec', '#fbd5d5', '#f3aeae', '#ea8383', '#e35f5f',
      '#df4747', '#c1121f', '#a30f1a', '#8a0c16', '#6f0911',
    ],
  },
})

createRoot(document.getElementById('root')).render(
  <React.StrictMode>
    <MantineProvider theme={theme} defaultColorScheme="light">
      <App />
    </MantineProvider>
  </React.StrictMode>
)

import { Route, Routes } from 'react-router';

export default function App() {
  return (
    <div className="app">
      <header className="app-header">
        <p className="app-name">The Discussion</p>
      </header>
      <main id="main" className="app-main">
        <Routes>
          <Route path="/" element={<h1>Discussions</h1>} />
          <Route path="*" element={<h1>Page not found</h1>} />
        </Routes>
      </main>
    </div>
  );
}

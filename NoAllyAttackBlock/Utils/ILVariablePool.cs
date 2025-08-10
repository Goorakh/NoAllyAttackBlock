using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using NoAllyAttackBlock.Utils.Extensions;
using System;
using System.Collections.Generic;

namespace NoAllyAttackBlock.Utils
{
    public class ILVariablePool
    {
        readonly ILContext _context;

        readonly List<VariableDefinition> _pooledVariables = [];

        readonly List<VariableDefinition> _borrowedVariables = [];

        public ILVariablePool(ILContext context)
        {
            _context = context;
        }

        public VariableDefinition GetOrCreate<T>()
        {
            return GetOrCreate(typeof(T));
        }

        public VariableDefinition GetOrCreate(Type variableType)
        {
            return GetOrCreate(_context.Import(variableType));
        }

        public VariableDefinition GetOrCreate(TypeReference variableType)
        {
            VariableDefinition variable = null;

            for (int i = 0; i < _pooledVariables.Count; i++)
            {
                VariableDefinition pooledVariable = _pooledVariables[i];
                if (pooledVariable.VariableType.FullName == variableType.FullName)
                {
                    variable = pooledVariable;
                    _pooledVariables.RemoveAt(i);
                    break;
                }
            }

            variable ??= _context.AddVariable(variableType);
            _borrowedVariables.Add(variable);
            return variable;
        }

        public void Return(VariableDefinition variable)
        {
            if (_borrowedVariables.Remove(variable))
            {
                _pooledVariables.Add(variable);
            }
        }

        public void ReturnAll()
        {
            for (int i = _borrowedVariables.Count - 1; i >= 0; i--)
            {
                Return(_borrowedVariables[i]);
            }
        }
    }
}

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

        readonly List<VariableDefinition> _variables = [];

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

            for (int i = 0; i < _variables.Count; i++)
            {
                VariableDefinition pooledVariable = _variables[i];
                if (pooledVariable.VariableType.FullName == variableType.FullName)
                {
                    variable = pooledVariable;
                    _variables.RemoveAt(i);
                    break;
                }
            }

            variable ??= _context.AddVariable(variableType);

            return variable;
        }

        public void Return(VariableDefinition variable)
        {
            _variables.Add(variable);
        }
    }
}
